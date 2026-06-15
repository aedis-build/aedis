using Aedis.Core.Extensions;
using Aedis.Exceptions;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Aedis.Messaging.RabbitMq;

/// <summary>
///     Broker RabbitMQ. Publica mensagens serializadas pela estratégia adequada
///     (<see cref="MessageSerializerResolver" />) e consome com ack/nack, retry e dead-letter.
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

    public Task PublishRawAsync(string exchange, string routingKey, ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream", string? correlationId = null,
        CancellationToken cancellationToken = default) {
        return ExecuteWithChannelAsync(async channel => {
            await CreateOrGetExchangeAsync(exchange, channel, cancellationToken);

            var props = new BasicProperties { Persistent = true, ContentType = contentType };
            if (correlationId is not null) props.CorrelationId = correlationId;

            await channel.BasicPublishAsync(exchange, routingKey, false, props, payload, cancellationToken);
            _logger.LogDebug("Payload bruto publicado em {Exchange}/{RoutingKey} ({ContentType})",
                exchange, routingKey, contentType);
        }, cancellationToken);
    }

    public async Task SubscribeAsync<T>(string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(retryOptions);

        var connection = await GetConnectionAsync();
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var queueName = await DeclareTopologyAsync(channel, queue, exchange, routingKey, retryOptions, cancellationToken);
        await channel.BasicQosAsync(0, _options.PrefetchCount, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, eventArgs) =>
            HandleDeliveryAsync(channel, queueName, handler, retryOptions, eventArgs, cancellationToken);

        await channel.BasicConsumeAsync(queueName, false, consumer, cancellationToken);
        _logger.LogDebug("Consumer registrado na fila {Queue} (exchange {Exchange}, routingKey {RoutingKey}, DLQ={Dlq})",
            queueName, exchange, routingKey, retryOptions.EnableDeadLetter);

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

    /// <summary>
    ///     Declara exchange + fila (quorum). Com dead-letter habilitado, declara também a DLX/DLQ e
    ///     configura a fila com <c>x-dead-letter-exchange</c> e <c>x-delivery-limit</c> (auto-DLQ após MaxRetries).
    /// </summary>
    private async Task<string> DeclareTopologyAsync(IChannel channel, string queue, string exchange, string routingKey,
        ConsumerRetryOptions retryOptions, CancellationToken cancellationToken) {
        var exchangeName = exchange.ToLowerInvariant();
        var queueName = queue.Sanitize().ToLowerInvariant();

        await CreateOrGetExchangeAsync(exchange, channel, cancellationToken);

        var queueArgs = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" };

        if (retryOptions.EnableDeadLetter) {
            var dlx = $"{exchangeName}.dlx";
            var dlq = $"{queueName}.dlq";

            await channel.ExchangeDeclareAsync(dlx, ExchangeType.Fanout, true, cancellationToken: cancellationToken);
            await channel.QueueDeclareAsync(dlq, true, false, false,
                new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(dlq, dlx, string.Empty, cancellationToken: cancellationToken);

            queueArgs["x-dead-letter-exchange"] = dlx;
            queueArgs["x-delivery-limit"] = retryOptions.MaxRetries;
        }

        await channel.QueueDeclareAsync(queueName, true, false, false, queueArgs, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queueName, exchangeName, routingKey, cancellationToken: cancellationToken);

        return queueName;
    }

    private async Task HandleDeliveryAsync<T>(IChannel channel, string queue, IMessageHandler<T> handler,
        ConsumerRetryOptions retryOptions, BasicDeliverEventArgs eventArgs, CancellationToken cancellationToken)
        where T : class, IMessage {
        try {
            if (Materialize<T>(eventArgs) is T message)
                await handler.HandleAsync(message, cancellationToken);

            await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
        }
        catch (OperationCanceledException) {
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, true, CancellationToken.None);
        }
        catch (PermanentFailureException ex) {
            _logger.LogError(ex, "Falha permanente ao processar mensagem da fila {Queue}.", queue);
            // Sem requeue: vai direto para a DLQ (se habilitada) ou é descartada com ACK.
            if (ex.SendToDeadLetterQueue && retryOptions.EnableDeadLetter)
                await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken);
            else
                await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Erro ao processar mensagem da fila {Queue}. Reenfileirando para nova tentativa.", queue);
            // Requeue: o x-delivery-limit do quorum envia para a DLQ após MaxRetries.
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, true, cancellationToken);
        }
    }

    /// <summary>
    ///     Reconstrói a mensagem do corpo recebido: se <typeparamref name="T" /> é <see cref="IRawMessage" />,
    ///     chama <c>FromRaw</c> (inverso simétrico de <c>ToData</c>); caso contrário, desserializa pelo
    ///     content-type via <see cref="MessageSerializerResolver" />.
    /// </summary>
    private object? Materialize<T>(BasicDeliverEventArgs eventArgs) where T : class, IMessage {
        if (typeof(IRawMessage).IsAssignableFrom(typeof(T))) {
            var raw = (IRawMessage)Activator.CreateInstance(typeof(T))!;
            raw.FromRaw(eventArgs.Body.ToArray(), eventArgs.BasicProperties.CorrelationId ?? string.Empty);
            return raw;
        }

        var serializer = _serializers.ResolveForContentType(eventArgs.BasicProperties.ContentType);
        return serializer.Deserialize(eventArgs.Body, typeof(T));
    }
}
