using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

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

    public async Task SubscribeAsync<T>(string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(retryOptions);

        var connection = await GetConnectionAsync();
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await CreateOrGetExchangeAsync(exchange, channel, cancellationToken);
        await CreateOrGetQueueAsync(queue, exchange, routingKey, channel, cancellationToken);
        await channel.BasicQosAsync(0, _options.PrefetchCount, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, eventArgs) => HandleDeliveryAsync(channel, queue, handler, retryOptions, eventArgs,
            cancellationToken);

        await channel.BasicConsumeAsync(queue, false, consumer, cancellationToken);
        _logger.LogDebug("Consumer registrado na fila {Queue} (exchange {Exchange}, routingKey {RoutingKey})",
            queue, exchange, routingKey);

        try {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) {
            // shutdown gracioso
        }
        finally {
            await channel.CloseAsync(CancellationToken.None);
        }
    }

    private async Task HandleDeliveryAsync<T>(IChannel channel, string queue, IMessageHandler<T> handler,
        ConsumerRetryOptions retryOptions, BasicDeliverEventArgs eventArgs, CancellationToken cancellationToken)
        where T : class, IMessage {
        try {
            var serializer = _serializers.ResolveForContentType(eventArgs.BasicProperties.ContentType);
            if (serializer.Deserialize(eventArgs.Body, typeof(T)) is T message)
                await handler.HandleAsync(message, cancellationToken);

            await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
        }
        catch (OperationCanceledException) {
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, true, CancellationToken.None);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Erro ao processar mensagem da fila {Queue}", queue);
            // Com DLQ habilitada, descarta (vai para a dead-letter se configurada); senão, requeue para nova tentativa.
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, !retryOptions.EnableDeadLetter, cancellationToken);
        }
    }
}
