using System.Collections.Concurrent;
using System.Text;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using IBM.WMQ;
using Microsoft.Extensions.Logging;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Gerencia os consumidores IBM MQ com ciclo de vida independente por fila: um loop dedicado que
///     alterna entre WAIT (bloqueia até chegar mensagem) e DRAIN (esvazia sem esperar), processa sob
///     syncpoint (commit no sucesso, backout no erro) e se auto-cura (detecta consumidores parados e os
///     reinicia). Mensagens que excedem <see cref="IbmMqOptions.BackoutThreshold" /> vão para a fila de
///     backout/DLQ — fechando o caminho que a implementação original deixava sem ligação.
/// </summary>
internal sealed class IbmMqConsumerManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ConsumerInfo> _consumers = new();
    private readonly SemaphoreSlim _disposeSemaphore = new(1, 1);
    private readonly ILogger _logger;
    private readonly IbmMqOptions _options;
    private readonly MessageSerializerResolver _serializers;
    private bool _disposed;

    public IbmMqConsumerManager(ILogger logger, IbmMqOptions options, MessageSerializerResolver serializers) {
        _logger = logger;
        _options = options;
        _serializers = serializers;
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;

        await _disposeSemaphore.WaitAsync();
        try {
            if (_disposed) return;

            _logger.LogDebug("Encerrando {Count} consumidores IBM MQ ativos.", _consumers.Count);
            await Task.WhenAll(_consumers.Keys.Select(id => StopConsumerAsync(id, CancellationToken.None)));
            _consumers.Clear();
            _disposed = true;
        }
        finally {
            _disposeSemaphore.Release();
        }
    }

    public async Task<string> StartConsumerAsync<T>(MQQueueManager queueManager, string queueName,
        IMessageHandler<T> handler, CancellationToken cancellationToken = default) where T : class, IMessage {
        var consumerId = Guid.NewGuid().ToString();
        var info = new ConsumerInfo<T> {
            ConsumerId = consumerId,
            QueueManager = queueManager,
            QueueName = queueName,
            Handler = handler,
            Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        };

        _consumers.TryAdd(consumerId, info);

        info.Task = Task.Run(async () => {
            try {
                await ConsumerLoopAsync(info);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Erro fatal no loop do consumer {ConsumerId}.", consumerId);
                info.IsDone = true;
            }
        }, info.Cts.Token);

        await Task.Delay(50, cancellationToken);
        return consumerId;
    }

    public Task StopConsumerAsync(string consumerId, CancellationToken cancellationToken = default) {
        if (_consumers.TryRemove(consumerId, out var info))
            try {
                info.Cts.Cancel();
                info.Cts.Dispose();
                _logger.LogDebug("Consumer IBM MQ {ConsumerId} parado.", consumerId);
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Erro ao parar o consumer IBM MQ {ConsumerId}.", consumerId);
            }

        return Task.CompletedTask;
    }

    public Task<bool> IsConsumerHealthyAsync(string consumerId) {
        if (!_consumers.TryGetValue(consumerId, out var info))
            return Task.FromResult(false);

        var healthy = !info.Cts.Token.IsCancellationRequested
                      && !info.IsDone
                      && info.Task is { IsCompleted: false, IsFaulted: false, IsCanceled: false };
        return Task.FromResult(healthy);
    }

    public async Task RestartUnhealthyConsumersAsync(CancellationToken cancellationToken = default) {
        foreach (var id in _consumers.Keys)
            if (!await IsConsumerHealthyAsync(id)) {
                _logger.LogWarning("Reiniciando o consumer IBM MQ não saudável {ConsumerId}.", id);
                await StopConsumerAsync(id, cancellationToken);
            }
    }

    private async Task ConsumerLoopAsync<T>(ConsumerInfo<T> info) where T : class, IMessage {
        var cancellationToken = info.Cts.Token;
        MQQueue? queue = null;

        try {
            var openOptions = MQC.MQOO_INPUT_SHARED | MQC.MQOO_FAIL_IF_QUIESCING;
            queue = info.QueueManager.AccessQueue(info.QueueName, openOptions);

            var syncpoint = _options.UseSyncpoint ? MQC.MQGMO_SYNCPOINT : MQC.MQGMO_NO_SYNCPOINT;
            var waitOptions = new MQGetMessageOptions {
                Options = MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | syncpoint,
                WaitInterval = _options.ConsumerWaitIntervalMs
            };
            var drainOptions = new MQGetMessageOptions {
                Options = MQC.MQGMO_NO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | syncpoint
            };

            var waitMode = true;
            _logger.LogDebug("Consumer {ConsumerId} iniciando o loop na fila {Queue}.", info.ConsumerId, info.QueueName);

            while (!cancellationToken.IsCancellationRequested)
                try {
                    var message = new MQMessage();
                    queue.Get(message, waitMode ? waitOptions : drainOptions);
                    waitMode = false;

                    await ProcessMessageAsync(info, message);
                }
                catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE) {
                    waitMode = true;
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (MQException ex) {
                    _logger.LogError(ex, "Erro MQ no consumer {ConsumerId}: ReasonCode={ReasonCode}.",
                        info.ConsumerId, ex.ReasonCode);

                    if (IsCriticalMqError(ex.ReasonCode)) {
                        _logger.LogCritical("Erro MQ crítico no consumer {ConsumerId} — encerrando.", info.ConsumerId);
                        break;
                    }

                    waitMode = true;
                    await Task.Delay(_options.ConsumerBackoffMs, cancellationToken);
                }
        }
        finally {
            try {
                queue?.Close();
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Erro ao fechar a fila do consumer {ConsumerId}.", info.ConsumerId);
            }

            info.IsDone = true;
            _logger.LogDebug("Loop do consumer {ConsumerId} finalizado.", info.ConsumerId);
        }
    }

    private async Task ProcessMessageAsync<T>(ConsumerInfo<T> info, MQMessage message) where T : class, IMessage {
        var queueManager = info.QueueManager;

        try {
            var deserialized = Deserialize<T>(message);

            if (deserialized is null) {
                _logger.LogWarning("Falha ao desserializar a mensagem no consumer {ConsumerId}.", info.ConsumerId);
                HandleFailure(message, queueManager, info.ConsumerId);
                return;
            }

            await info.Handler.HandleAsync(deserialized, info.Cts.Token);

            if (_options.UseSyncpoint) queueManager.Commit();
            _logger.LogDebug("Mensagem processada pelo consumer {ConsumerId}.", info.ConsumerId);
        }
        catch (OperationCanceledException) {
            _logger.LogDebug("Processamento cancelado no consumer {ConsumerId} — mensagem volta à fila.",
                info.ConsumerId);
            if (_options.UseSyncpoint) queueManager.Backout();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Erro ao processar a mensagem no consumer {ConsumerId}.", info.ConsumerId);
            HandleFailure(message, queueManager, info.ConsumerId);
        }
    }

    /// <summary>
    ///     Mensagem com falha: se atingiu o limite de backout e a DLQ está ligada, move para a fila de
    ///     backout/DLQ e confirma; senão faz backout para o IBM MQ reentregar (incrementando BackoutCount).
    /// </summary>
    private void HandleFailure(MQMessage message, MQQueueManager queueManager, string consumerId) {
        if (_options.EnableDeadLetterQueue && message.BackoutCount >= _options.BackoutThreshold) {
            MoveToBackoutQueue(message, queueManager, consumerId);
            return;
        }

        if (_options.UseSyncpoint) queueManager.Backout();
    }

    private void MoveToBackoutQueue(MQMessage message, MQQueueManager queueManager, string consumerId) {
        var target = !string.IsNullOrEmpty(_options.DeadLetterQueueName)
            ? _options.DeadLetterQueueName!
            : _options.BackoutQueue;

        try {
            using var dlq = queueManager.AccessQueue(target, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING);
            dlq.Put(message, new MQPutMessageOptions {
                Options = MQC.MQPMO_SYNCPOINT | MQC.MQPMO_FAIL_IF_QUIESCING
            });
            queueManager.Commit();

            _logger.LogWarning(
                "Mensagem movida para a fila de backout {Queue} pelo consumer {ConsumerId} após {Backout} tentativas.",
                target, consumerId, message.BackoutCount);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Falha ao mover a mensagem para a fila de backout {Queue}.", target);
            if (_options.UseSyncpoint) queueManager.Backout();
        }
    }

    /// <summary>
    ///     Desserializa a mensagem em dois caminhos: o bruto, para tipos <see cref="IRawMessage" /> (preserva
    ///     o binário e injeta o MQMD via <see cref="IMqMetadataMessage" /> quando suportado); e o estruturado,
    ///     que resolve o serializador pelo content-type (JSON como fallback). Devolve null em falha.
    /// </summary>
    private T? Deserialize<T>(MQMessage message) where T : class, IMessage {
        if (typeof(IRawMessage).IsAssignableFrom(typeof(T))) {
            try {
                var instance = Activator.CreateInstance<T>();
                var rawData = message.ReadBytes(message.MessageLength);
                var rawCorrelationId = Encoding.UTF8.GetString(message.CorrelationId).Trim().TrimEnd('\0');
                ((IRawMessage)instance).FromRaw(rawData, rawCorrelationId);

                if (instance is IMqMetadataMessage metadataMessage)
                    metadataMessage.FromMqMetadata(MqMessageFactory.ReadMetadata(message));

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
        public string ConsumerId { get; init; } = string.Empty;
        public MQQueueManager QueueManager { get; init; } = null!;
        public string QueueName { get; init; } = string.Empty;
        public CancellationTokenSource Cts { get; init; } = null!;
        public Task? Task { get; set; }
        public bool IsDone { get; set; }
    }

    private sealed class ConsumerInfo<T> : ConsumerInfo where T : class, IMessage
    {
        public IMessageHandler<T> Handler { get; init; } = null!;
    }
}
