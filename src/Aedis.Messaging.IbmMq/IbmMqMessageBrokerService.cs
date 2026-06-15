using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using IBM.WMQ;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Broker IBM MQ do Aedis. Publica mensagens montando o MQMD a partir das <see cref="IbmMqOptions" />
///     (reports, tipo, persistência, formato, CCSID) e serializando o payload via
///     <see cref="MessageSerializerResolver" /> — sem a restrição de "deve ser byte[]" da implementação
///     original. O consumo (subscribe) chega na fatia do consumer manager.
/// </summary>
public sealed class IbmMqMessageBrokerService : IbmMqBaseService, IMessageBrokerService
{
    private readonly IbmMqConsumerManager _consumerManager;
    private readonly MessageSerializerResolver _serializers;

    public IbmMqMessageBrokerService(IOptions<IbmMqOptions> options, ILogger<IbmMqMessageBrokerService> logger,
        MessageSerializerResolver? serializers = null, ILoggerFactory? loggerFactory = null)
        : base(options, logger) {
        _serializers = serializers ?? MessageSerializerResolver.CreateDefault();
        var consumerLogger = loggerFactory?.CreateLogger<IbmMqConsumerManager>() ?? (ILogger)logger;
        _consumerManager = new IbmMqConsumerManager(consumerLogger, options.Value, _serializers);
    }

    public Task PublishAsync<T>(string exchange, string routingKey, T message,
        CancellationToken cancellationToken = default)
        where T : class, IMessage {
        return ExecuteWithSessionAsync(async queueManager => {
            var queueName = ResolveQueue(exchange, routingKey);
            using var queue = OpenQueue(queueManager, queueName);

            try {
                var data = message.ToData();
                var serializer = _serializers.ResolveForSerialize(data);
                var body = serializer.Serialize(data);

                var putMessage = MqMessageFactory.BuildMqMessage(_options, message.CorrelationId);
                putMessage.Write(body.ToArray());

                queue.Put(putMessage, BuildPutMessageOptions());
                if (_options.UseSyncpoint) queueManager.Commit();

                WarnIfReportsWithoutReplyTo();
                _logger.LogDebug("[IBM MQ] PUBLISH {EventName} -> {Queue} (MsgId={MsgId}, {ContentType}).",
                    message.EventName, queueName, ToHex(putMessage.MessageId), serializer.ContentType);

                await Task.CompletedTask;
            }
            catch (MQException ex) {
                _logger.LogError(ex, "Falha ao publicar a mensagem {CorrelationId} no IBM MQ.", message.CorrelationId);
                throw;
            }
        }, cancellationToken);
    }

    public Task PublishRawAsync(string exchange, string routingKey, ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream", string? correlationId = null,
        CancellationToken cancellationToken = default) {
        return ExecuteWithSessionAsync(async queueManager => {
            var queueName = ResolveQueue(exchange, routingKey);
            using var queue = OpenQueue(queueManager, queueName);

            try {
                var putMessage = MqMessageFactory.BuildMqMessage(_options, correlationId);
                putMessage.Write(payload.ToArray());

                queue.Put(putMessage, BuildPutMessageOptions());
                if (_options.UseSyncpoint) queueManager.Commit();

                WarnIfReportsWithoutReplyTo();
                _logger.LogDebug("[IBM MQ] PUBLISH raw -> {Queue} (MsgId={MsgId}, {ContentType}).",
                    queueName, ToHex(putMessage.MessageId), contentType);

                await Task.CompletedTask;
            }
            catch (MQException ex) {
                _logger.LogError(ex, "Falha ao publicar payload bruto no IBM MQ ({CorrelationId}).", correlationId);
                throw;
            }
        }, cancellationToken);
    }

    public async Task SubscribeAsync<T>(string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(retryOptions);

        var queueName = string.IsNullOrWhiteSpace(queue) ? exchange : queue;
        var queueManager = await EnsureConnectionAsync();

        var consumerId = await _consumerManager.StartConsumerAsync(queueManager, queueName, handler, cancellationToken);
        _logger.LogDebug("Consumer IBM MQ {ConsumerId} iniciado na fila {Queue}.", consumerId, queueName);

        await KeepConsumerAliveAsync(consumerId, cancellationToken);
    }

    private async Task KeepConsumerAliveAsync(string consumerId, CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested) {
                if (!await _consumerManager.IsConsumerHealthyAsync(consumerId)) {
                    _logger.LogWarning("Consumer {ConsumerId} não está saudável — tentando reiniciar.", consumerId);
                    await _consumerManager.RestartUnhealthyConsumersAsync(cancellationToken);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(_options.ConsumerHealthCheckIntervalMs), cancellationToken);
            }
        }
        catch (OperationCanceledException) {
            _logger.LogDebug("Encerramento solicitado para o consumer {ConsumerId}.", consumerId);
        }
        finally {
            await _consumerManager.StopConsumerAsync(consumerId, CancellationToken.None);
        }
    }

    public override async ValueTask DisposeAsync() {
        await _consumerManager.DisposeAsync();
        await base.DisposeAsync();
    }

    private static string ResolveQueue(string exchange, string routingKey) =>
        string.IsNullOrWhiteSpace(routingKey) ? exchange : routingKey;

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    private void WarnIfReportsWithoutReplyTo() {
        if (_options.EnableReports
            && (_options.Reports.Coa || _options.Reports.Cod || _options.Reports.CoaWithData || _options.Reports.CodWithData)
            && string.IsNullOrEmpty(_options.ReplyToReportQueueAlias))
            _logger.LogWarning(
                "Reports COA/COD ativos mas ReplyToReportQueueAlias não configurado — o Queue Manager não " +
                "saberá onde entregar as confirmações.");
    }
}
