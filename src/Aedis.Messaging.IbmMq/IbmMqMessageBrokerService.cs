using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Aedis.Messaging.Telemetry;
using IBM.WMQ;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Broker IBM MQ do Aedis. Publica numa conexão exclusiva do pool de publicação (PUT + commit isolados),
///     montando o MQMD a partir das <see cref="IbmMqOptions" /> e serializando o payload via
///     <see cref="MessageSerializerResolver" /> (JSON e payloads brutos). O consumo sobe workers em threads
///     dedicadas por fila (<see cref="IbmMqConsumerManager" />) e os mantém vivos pelo tempo da assinatura.
///     Cada publish e cada mensagem consumida geram span e métricas via <see cref="MessagingInstrumentation" />.
/// </summary>
public sealed class IbmMqMessageBrokerService : IbmMqBaseService, IMessageBrokerService
{
    private readonly IbmMqConsumerManager _consumerManager;
    private readonly MessageSerializerResolver _serializers;

    /// <summary>
    ///     Cria o broker com o resolvedor de serializadores (padrão quando ausente) e o gerenciador de
    ///     consumers, que compartilha o teto de conexões do processo. Nada conecta aqui.
    /// </summary>
    public IbmMqMessageBrokerService(IOptions<IbmMqOptions> options, ILogger<IbmMqMessageBrokerService> logger,
        MessageSerializerResolver? serializers = null, ILoggerFactory? loggerFactory = null)
        : base(options, logger) {
        _serializers = serializers ?? MessageSerializerResolver.CreateDefault();
        var consumerLogger = loggerFactory?.CreateLogger<IbmMqConsumerManager>() ?? (ILogger)logger;
        _consumerManager = new IbmMqConsumerManager(consumerLogger, options.Value, ConnectionBudget, _serializers);

        WarnIfReportsWithoutReplyTo();
    }

    /// <summary>
    ///     Publica uma mensagem tipada: serializa o payload, monta o MQMD e faz PUT na fila resolvida (routing
    ///     key, ou exchange quando ela é vazia), confirmando quando o syncpoint está ligado.
    /// </summary>
    public Task PublishAsync<T>(string exchange, string routingKey, T message,
        CancellationToken cancellationToken = default)
        where T : class, IMessage {
        var queueName = ResolveQueue(exchange, routingKey);

        return ExecuteWithSessionAsync(queueManager => {
            using var operation = MessagingInstrumentation.StartPublish(IbmMqConsumerManager.SystemName, queueName, message, _logger);
            using var queue = OpenQueue(queueManager, queueName);

            try {
                var data = message.ToData();
                var serializer = _serializers.ResolveForSerialize(data);
                var body = serializer.Serialize(data);

                var putMessage = MqMessageFactory.BuildMqMessage(_options, message.CorrelationId);
                putMessage.Write(body.ToArray());

                queue.Put(putMessage, BuildPutMessageOptions());
                if (_options.UseSyncpoint) queueManager.Commit();

                operation.SetTag("messaging.message.id", Convert.ToHexString(putMessage.MessageId));
                _logger.LogDebug("[IBM MQ] PUBLISH {EventName} -> {Queue} ({ContentType}).",
                    message.EventName, queueName, serializer.ContentType);
            }
            catch (Exception ex) {
                operation.Complete(MessageOutcome.UnhandledFailure, ex);
                _logger.LogError(ex, "Falha ao publicar a mensagem {CorrelationId} no IBM MQ.", message.CorrelationId);
                throw;
            }

            return Task.CompletedTask;
        }, cancellationToken);
    }

    /// <summary>Publica um payload bruto (bytes) sem serializador, montando o MQMD a partir das opções.</summary>
    public Task PublishRawAsync(string exchange, string routingKey, ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream", string? correlationId = null,
        CancellationToken cancellationToken = default) {
        var queueName = ResolveQueue(exchange, routingKey);

        return ExecuteWithSessionAsync(queueManager => {
            using var queue = OpenQueue(queueManager, queueName);

            try {
                var putMessage = MqMessageFactory.BuildMqMessage(_options, correlationId);
                putMessage.Write(payload.ToArray());

                queue.Put(putMessage, BuildPutMessageOptions());
                if (_options.UseSyncpoint) queueManager.Commit();

                _logger.LogDebug("[IBM MQ] PUBLISH raw -> {Queue} ({ContentType}).", queueName, contentType);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Falha ao publicar payload bruto no IBM MQ ({CorrelationId}).", correlationId);
                throw;
            }

            return Task.CompletedTask;
        }, cancellationToken);
    }

    /// <summary>
    ///     Assina uma fila (usa o exchange quando <paramref name="queue" /> é vazio): reserva a concorrência no
    ///     teto, sobe os workers e os mantém vivos — religando consumers mortos a cada
    ///     <see cref="IbmMqOptions.ConsumerHealthCheckIntervalMs" /> — até o cancelamento. Falha imediatamente
    ///     se a concorrência da fila não cabe em <see cref="IbmMqOptions.MaxConnections" />.
    /// </summary>
    public async Task SubscribeAsync<T>(string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(retryOptions);

        var queueName = string.IsNullOrWhiteSpace(queue) ? exchange : queue;
        var consumerId = await _consumerManager.StartConsumerAsync(queueName, handler, cancellationToken);
        _logger.LogDebug("Consumer IBM MQ {ConsumerId} iniciado na fila {Queue}.", consumerId, queueName);

        await KeepConsumerAliveAsync(consumerId, cancellationToken);
    }

    /// <summary>Encerra todos os consumers e o pool de publicação.</summary>
    public override async ValueTask DisposeAsync() {
        await _consumerManager.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task KeepConsumerAliveAsync(string consumerId, CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested) {
                await Task.Delay(TimeSpan.FromMilliseconds(_options.ConsumerHealthCheckIntervalMs), cancellationToken);

                if (!_consumerManager.IsConsumerHealthy(consumerId)) {
                    _logger.LogWarning("Consumer IBM MQ {ConsumerId} não está saudável; religando.", consumerId);
                    await _consumerManager.RestartUnhealthyConsumersAsync();
                }
            }
        }
        catch (OperationCanceledException) {
            _logger.LogDebug("Encerramento solicitado para o consumer IBM MQ {ConsumerId}.", consumerId);
        }
        finally {
            await _consumerManager.StopConsumerAsync(consumerId);
        }
    }

    private static string ResolveQueue(string exchange, string routingKey) =>
        string.IsNullOrWhiteSpace(routingKey) ? exchange : routingKey;

    private void WarnIfReportsWithoutReplyTo() {
        if (_options.EnableReports
            && (_options.Reports.Coa || _options.Reports.Cod || _options.Reports.CoaWithData || _options.Reports.CodWithData)
            && string.IsNullOrEmpty(_options.ReplyToReportQueueAlias))
            _logger.LogWarning(
                "Reports COA/COD ativos mas ReplyToReportQueueAlias não configurado — o Queue Manager não saberá onde entregar as confirmações.");
    }
}
