using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Aedis.Messaging.RabbitMq;

/// <summary>
///     Broker RabbitMQ. Publica mensagens serializadas pela estratégia adequada
///     (<see cref="MessageSerializerResolver" />). O consumo (subscribe) é adicionado na próxima fatia.
/// </summary>
public class RabbitMqMessageBrokerService : RabbitMqBaseService, IMessageBrokerService
{
    private readonly ILogger<RabbitMqMessageBrokerService> _logger;
    private readonly MessageSerializerResolver _serializers;

    public RabbitMqMessageBrokerService(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqMessageBrokerService> logger,
        MessageSerializerResolver? serializers = null)
        : base(options, logger) {
        _logger = logger;
        _serializers = serializers ?? MessageSerializerResolver.CreateDefault();
    }

    public Task PublishAsync<T>(string exchange, string routingKey, T message,
        CancellationToken cancellationToken = default)
        where T : class, IMessage {
        return ExecuteWithChannelAsync(async channel => {
            await CreateOrGetExchangeAsync(exchange, channel, cancellationToken);

            var data = message.ToData();
            var serializer = _serializers.ResolveForSerialize(data);
            var body = serializer.Serialize(data);

            var props = new BasicProperties {
                Persistent = true,
                ContentType = serializer.ContentType,
                CorrelationId = message.CorrelationId
            };

            await channel.BasicPublishAsync(exchange, routingKey, false, props, body, cancellationToken);

            _logger.LogDebug("Mensagem {EventName} publicada em {Exchange}/{RoutingKey} ({ContentType})",
                message.EventName, exchange, routingKey, serializer.ContentType);
        }, cancellationToken);
    }

    public Task SubscribeAsync<T>(string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage {
        throw new NotImplementedException("O consumo (subscribe) será adicionado na fatia 3 do RabbitMq.");
    }
}
