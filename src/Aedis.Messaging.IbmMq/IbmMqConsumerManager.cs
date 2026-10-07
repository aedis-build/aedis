using System.Collections.Concurrent;
using System.Text;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Aedis.Messaging.Telemetry;
using IBM.WMQ;
using Microsoft.Extensions.Logging;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Gerencia os consumers IBM MQ. Cada fila assinada ganha um pool exclusivo de conexões e
///     <c>ConsumerConcurrency</c> workers, cada um numa <strong>thread dedicada com loop síncrono</strong>: o
///     cliente IBM MQ não tem API assíncrona (connect e <c>Get</c> bloqueiam), então um <c>Task.Run</c> deixaria
///     threads do pool presas no GET em espera e no connect durante indisponibilidade do QM, afogando o resto
///     do processo. O loop alterna WAIT (bloqueia até chegar mensagem) e DRAIN (esvazia sem esperar), entrega
///     ao handler e resolve a transação na própria conexão: retorno normal confirma, exceção faz backout.
///     Consumers mortos são religados pelo keep-alive do broker, devolvendo e reservando de novo no teto.
/// </summary>
internal sealed class IbmMqConsumerManager : IAsyncDisposable
{
    internal const string SystemName = "ibm_mq";
    private const int StopGracePeriodMs = 3000;

    private readonly IbmMqConnectionBudget _budget;
    private readonly ConcurrentDictionary<string, ConsumerInfo> _consumers = new();
    private readonly SemaphoreSlim _disposeSemaphore = new(1, 1);
    private readonly ILogger _logger;
    private readonly IbmMqOptions _options;
    private readonly MessageSerializerResolver _serializers;
    private bool _disposed;

    internal IbmMqConsumerManager(ILogger logger, IbmMqOptions options, IbmMqConnectionBudget budget,
        MessageSerializerResolver serializers) {
        _logger = logger;
        _options = options;
        _budget = budget;
        _serializers = serializers;
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;

        await _disposeSemaphore.WaitAsync();
        try {
            if (_disposed) return;

            _logger.LogDebug("Encerrando {Count} consumer(s) IBM MQ.", _consumers.Count);
            await Task.WhenAll(_consumers.Keys.Select(id => StopConsumerAsync(id)));
            _consumers.Clear();
            _disposed = true;
        }
        finally {
            _disposeSemaphore.Release();
        }
    }

    /// <summary>
    ///     Inicia um consumer para a fila: reserva a concorrência no teto, cria o pool exclusivo e sobe os
    ///     workers em threads dedicadas. Devolve o identificador do consumer.
    /// </summary>
    internal Task<string> StartConsumerAsync<T>(string queueName, IMessageHandler<T> handler,
        CancellationToken cancellationToken = default) where T : class, IMessage {
        var info = new ConsumerInfo<T>(Guid.NewGuid().ToString("N"), queueName, handler, cancellationToken);
        Launch(info);
        return Task.FromResult(info.ConsumerId);
    }

    /// <summary>
    ///     Para o consumer: cancela, dá um curto período de graça para workers em processamento concluírem
    ///     (commit) e fecha o pool — o que destrava quem está num <c>Get</c> em espera. Devolve a concorrência
    ///     ao teto.
    /// </summary>
    internal async Task StopConsumerAsync(string consumerId) {
        if (!_consumers.TryRemove(consumerId, out var info)) return;

        await ShutdownAsync(info);
        _logger.LogDebug("Consumer IBM MQ {ConsumerId} parado.", consumerId);
    }

    internal bool IsConsumerHealthy(string consumerId) {
        if (!_consumers.TryGetValue(consumerId, out var info)) return false;

        return !info.Cts.IsCancellationRequested
               && !info.IsDisposed
               && info.Workers is { IsCompleted: false };
    }

    /// <summary>
    ///     Religa, no lugar e com o mesmo identificador, todo consumer cujos workers morreram: para o antigo
    ///     (devolvendo ao teto) e sobe os workers de novo, salvo se o cancelamento original já foi pedido.
    /// </summary>
    internal async Task RestartUnhealthyConsumersAsync() {
        foreach (var (consumerId, info) in _consumers.ToArray()) {
            if (IsConsumerHealthy(consumerId)) continue;

            _logger.LogWarning("Religando o consumer IBM MQ {ConsumerId} da fila {Queue}.", consumerId, info.QueueName);
            if (_consumers.TryRemove(consumerId, out var removed))
                await ShutdownAsync(removed);

            if (info.ParentToken.IsCancellationRequested) continue;

            try {
                Launch(info);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Falha ao religar o consumer IBM MQ {ConsumerId} da fila {Queue}.", consumerId, info.QueueName);
            }
        }
    }

    private void Launch(ConsumerInfo info) {
        var concurrency = _budget.Reserve($"fila {info.QueueName}", ResolveConcurrency(info.QueueName));
        info.Pool = new IbmMqConnectionPool(_options, _logger, concurrency);
        info.Cts = CancellationTokenSource.CreateLinkedTokenSource(info.ParentToken);
        info.IsDisposed = false;

        var workers = Enumerable.Range(0, concurrency)
            .Select(index => Task.Factory.StartNew(() => info.RunWorker(this, index), info.Cts.Token,
                TaskCreationOptions.LongRunning, TaskScheduler.Default))
            .ToArray();

        info.Workers = Task.WhenAll(workers);
        _consumers[info.ConsumerId] = info;

        _logger.LogDebug("Consumer IBM MQ {ConsumerId} iniciado com {Concurrency} worker(s) na fila {Queue}.",
            info.ConsumerId, concurrency, info.QueueName);
    }

    private async Task ShutdownAsync(ConsumerInfo info) {
        try {
            info.Cts.Cancel();
            if (info.Workers is { } workers)
                await Task.WhenAny(workers, Task.Delay(StopGracePeriodMs));
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Erro ao parar o consumer IBM MQ {ConsumerId}.", info.ConsumerId);
        }
        finally {
            info.Pool?.Dispose();
            info.Cts.Dispose();
            info.IsDisposed = true;
            _budget.Release(info.Pool?.MaxSize ?? 0);
        }
    }

    private int ResolveConcurrency(string queueName) {
        var configured = _options.QueueConcurrency.TryGetValue(queueName, out var perQueue)
            ? perQueue
            : _options.ConsumerConcurrency;

        return Math.Max(1, configured);
    }

    private void WorkerLoop<T>(ConsumerInfo<T> info, int workerIndex) where T : class, IMessage {
        var cancellationToken = info.Cts.Token;

        while (!cancellationToken.IsCancellationRequested) {
            MQQueueManager? connection = null;
            MQQueue? queue = null;

            try {
                connection = info.Pool!.Checkout(cancellationToken);
                queue = connection.AccessQueue(info.QueueName, MQC.MQOO_INPUT_SHARED | MQC.MQOO_INQUIRE | MQC.MQOO_FAIL_IF_QUIESCING);
                _logger.LogDebug("Fila {Queue} aberta pelo worker {Worker} do consumer {ConsumerId}.",
                    info.QueueName, workerIndex, info.ConsumerId);

                DrainQueue(info, connection, queue, workerIndex);
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (ObjectDisposedException) {
                break;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Worker {Worker} do consumer IBM MQ {ConsumerId} falhou; reestabelecendo.",
                    workerIndex, info.ConsumerId);
            }
            finally {
                try {
                    queue?.Close();
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, "Erro ao fechar a fila do worker {Worker} do consumer {ConsumerId}.",
                        workerIndex, info.ConsumerId);
                }

                info.Pool?.Checkin(connection);
            }

            if (cancellationToken.WaitHandle.WaitOne(_options.ConsumerBackoffMs))
                break;
        }

        _logger.LogDebug("Worker {Worker} do consumer IBM MQ {ConsumerId} encerrado.", workerIndex, info.ConsumerId);
    }

    private void DrainQueue<T>(ConsumerInfo<T> info, MQQueueManager connection, MQQueue queue, int workerIndex)
        where T : class, IMessage {
        var cancellationToken = info.Cts.Token;
        var syncpoint = _options.UseSyncpoint ? MQC.MQGMO_SYNCPOINT : MQC.MQGMO_NO_SYNCPOINT;
        var waitOptions = new MQGetMessageOptions {
            Options = MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | syncpoint,
            WaitInterval = _options.ConsumerWaitIntervalMs
        };
        var drainOptions = new MQGetMessageOptions {
            Options = MQC.MQGMO_NO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | syncpoint
        };
        var waitMode = true;

        while (!cancellationToken.IsCancellationRequested)
            try {
                var message = new MQMessage();
                queue.Get(message, waitMode ? waitOptions : drainOptions);
                waitMode = false;

                ProcessMessageAsync(info, message, connection).GetAwaiter().GetResult();
            }
            catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE) {
                waitMode = true;
            }
            catch (OperationCanceledException) {
                return;
            }
            catch (MQException ex) {
                _logger.LogError(ex, "Erro MQ no worker {Worker} do consumer {ConsumerId}: ReasonCode={ReasonCode}.",
                    workerIndex, info.ConsumerId, ex.ReasonCode);

                if (IsCriticalMqError(ex.ReasonCode)) {
                    _logger.LogCritical("Erro MQ crítico no worker {Worker} do consumer {ConsumerId}; reestabelecendo a conexão.",
                        workerIndex, info.ConsumerId);
                    return;
                }

                waitMode = true;
                if (cancellationToken.WaitHandle.WaitOne(_options.ConsumerBackoffMs))
                    return;
            }
    }

    private async Task ProcessMessageAsync<T>(ConsumerInfo<T> info, MQMessage message, MQQueueManager connection)
        where T : class, IMessage {
        var transportCorrelationId = ReadCorrelationId(message);
        using var operation = MessagingInstrumentation.StartProcess(SystemName, info.QueueName, null,
            transportCorrelationId, Convert.ToHexString(message.MessageId), message.BackoutCount + 1, _logger);
        operation.SetTag("aedis.mq.feedback", message.Feedback);
        operation.SetTag("aedis.mq.message_type", message.MessageType);

        try {
            var deserialized = Deserialize<T>(message);
            if (deserialized is null) {
                var failure = new InvalidOperationException("Falha ao desserializar a mensagem IBM MQ.");
                operation.Complete(MessageOutcome.PermanentFailure, failure);
                HandleFailure(message, connection, info.ConsumerId);
                return;
            }

            operation.SetMessage(deserialized);
            await info.Handler.HandleAsync(deserialized, info.Cts.Token);

            if (_options.UseSyncpoint) connection.Commit();
            operation.Complete(MessageOutcome.Success);
            _logger.LogDebug("Mensagem processada pelo consumer IBM MQ {ConsumerId}.", info.ConsumerId);
        }
        catch (OperationCanceledException ex) {
            operation.Complete(MessageOutcome.Cancelled, ex);
            _logger.LogDebug("Processamento cancelado no consumer {ConsumerId}; a mensagem volta à fila.", info.ConsumerId);
            SafeBackout(connection, info.ConsumerId);
            throw;
        }
        catch (Exception ex) {
            operation.Complete(MessageOutcomeClassifier.FromException(ex), ex);
            _logger.LogError(ex, "Erro ao processar a mensagem no consumer IBM MQ {ConsumerId}.", info.ConsumerId);
            HandleFailure(message, connection, info.ConsumerId);
        }
    }

    private void HandleFailure(MQMessage message, MQQueueManager connection, string consumerId) {
        if (_options.EnableDeadLetterQueue && message.BackoutCount >= _options.BackoutThreshold) {
            MoveToDeadLetter(message, connection, consumerId);
            return;
        }

        SafeBackout(connection, consumerId);
    }

    private void MoveToDeadLetter(MQMessage message, MQQueueManager connection, string consumerId) {
        var target = _options.DeadLetterQueueName!;

        try {
            using var dlq = connection.AccessQueue(target, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING);
            dlq.Put(message, new MQPutMessageOptions { Options = MQC.MQPMO_SYNCPOINT | MQC.MQPMO_FAIL_IF_QUIESCING });
            connection.Commit();

            _logger.LogWarning("Mensagem movida para a dead-letter {Queue} pelo consumer {ConsumerId} após {Backouts} backout(s).",
                target, consumerId, message.BackoutCount);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Falha ao mover a mensagem para a dead-letter {Queue}.", target);
            SafeBackout(connection, consumerId);
        }
    }

    private void SafeBackout(MQQueueManager connection, string consumerId) {
        if (!_options.UseSyncpoint) return;

        try {
            connection.Backout();
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Erro no backout da mensagem no consumer {ConsumerId}.", consumerId);
        }
    }

    private T? Deserialize<T>(MQMessage message) where T : class, IMessage {
        if (typeof(IRawMessage).IsAssignableFrom(typeof(T))) {
            try {
                var instance = Activator.CreateInstance<T>();
                var rawData = message.ReadBytes(message.MessageLength);
                ((IRawMessage)instance).FromRaw(rawData, ReadCorrelationId(message) ?? string.Empty);

                if (instance is IMqMetadataMessage metadataMessage)
                    metadataMessage.FromProviderMetadata(MqMessageFactory.ReadMetadata(message));

                return instance;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Falha ao reconstruir mensagem bruta IBM MQ.");
                return null;
            }
        }

        try {
            var bytes = message.ReadBytes(message.MessageLength);
            var serializer = _serializers.ResolveForContentType(null);
            return serializer.Deserialize(bytes, typeof(T)) as T;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Falha ao desserializar mensagem IBM MQ para {Type}.", typeof(T).Name);
            return null;
        }
    }

    private static string? ReadCorrelationId(MQMessage message) {
        try {
            var text = Encoding.UTF8.GetString(message.CorrelationId).Trim().TrimEnd('\0');
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch {
            return null;
        }
    }

    private static bool IsCriticalMqError(int reasonCode) => reasonCode switch {
        MQC.MQRC_Q_MGR_NOT_AVAILABLE => true,
        MQC.MQRC_Q_MGR_QUIESCING => true,
        MQC.MQRC_Q_MGR_STOPPING => true,
        MQC.MQRC_CONNECTION_BROKEN => true,
        MQC.MQRC_CONNECTION_QUIESCING => true,
        MQC.MQRC_NOT_AUTHORIZED => true,
        _ => false
    };

    private abstract class ConsumerInfo
    {
        protected ConsumerInfo(string consumerId, string queueName, CancellationToken parentToken) {
            ConsumerId = consumerId;
            QueueName = queueName;
            ParentToken = parentToken;
            Cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        }

        public string ConsumerId { get; }
        public string QueueName { get; }
        public CancellationToken ParentToken { get; }
        public IbmMqConnectionPool? Pool { get; set; }
        public CancellationTokenSource Cts { get; set; }
        public Task? Workers { get; set; }
        public bool IsDisposed { get; set; }

        public abstract void RunWorker(IbmMqConsumerManager manager, int workerIndex);
    }

    private sealed class ConsumerInfo<T> : ConsumerInfo where T : class, IMessage
    {
        public ConsumerInfo(string consumerId, string queueName, IMessageHandler<T> handler, CancellationToken parentToken)
            : base(consumerId, queueName, parentToken) {
            Handler = handler;
        }

        public IMessageHandler<T> Handler { get; }

        public override void RunWorker(IbmMqConsumerManager manager, int workerIndex) => manager.WorkerLoop(this, workerIndex);
    }
}
