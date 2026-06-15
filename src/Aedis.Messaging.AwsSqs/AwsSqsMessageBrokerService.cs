using System.Text.Json;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using SnsMessageAttributeValue = Amazon.SimpleNotificationService.Model.MessageAttributeValue;
using SqsMessageAttributeValue = Amazon.SQS.Model.MessageAttributeValue;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Broker AWS SQS/SNS do Aedis. Detecta de forma transparente se o exchange é um SNS Topic (pub/sub)
///     ou uma SQS Queue (point-to-point) e auto-provisiona os recursos no subscribe. O payload é
///     serializado via <see cref="MessageSerializerResolver" /> e codificado em base64 (o corpo do
///     SNS/SQS é texto), com o content-type em um atributo de mensagem.
/// </summary>
public sealed class AwsSqsMessageBrokerService : IMessageBrokerService
{
    private readonly AwsSqsAdministrationHelper _adminHelper;
    private readonly AwsSqsConsumerManager _consumerManager;
    private readonly IAwsPubSubFactory _factory;
    private readonly ILogger<AwsSqsMessageBrokerService> _logger;
    private readonly MessageSerializerResolver _serializers;

    public AwsSqsMessageBrokerService(IAwsPubSubFactory factory, ILogger<AwsSqsMessageBrokerService> logger,
        AwsSqsAdministrationHelper adminHelper, AwsSqsConsumerManager consumerManager,
        MessageSerializerResolver? serializers = null) {
        _factory = factory;
        _logger = logger;
        _adminHelper = adminHelper;
        _consumerManager = consumerManager;
        _serializers = serializers ?? MessageSerializerResolver.CreateDefault();
    }

    public async Task PublishAsync<T>(string exchange, string routingKey, T message,
        CancellationToken cancellationToken = default) where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(message);

        var data = message.ToData();
        var serializer = _serializers.ResolveForSerialize(data);
        var body = Convert.ToBase64String(serializer.Serialize(data).ToArray());

        await PublishBodyAsync(exchange, routingKey, body, serializer.ContentType, message.CorrelationId,
            cancellationToken);
    }

    public async Task PublishRawAsync(string exchange, string routingKey, ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream", string? correlationId = null,
        CancellationToken cancellationToken = default) {
        var body = Convert.ToBase64String(payload.ToArray());
        await PublishBodyAsync(exchange, routingKey, body, contentType, correlationId ?? Guid.NewGuid().ToString(),
            cancellationToken);
    }

    public Task SubscribeAsync<T>(string queue, string exchange, string routingKey, IMessageHandler<T> handler,
        ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default) where T : class, IMessage =>
        SubscribeCoreAsync(queue, exchange,
            string.IsNullOrWhiteSpace(routingKey) ? [] : [routingKey], handler, retryOptions, cancellationToken);

    public Task SubscribeAsync<T>(string queue, string exchange, IEnumerable<string> routingKeys,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage =>
        SubscribeCoreAsync(queue, exchange, routingKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),
            handler, retryOptions, cancellationToken);

    private async Task SubscribeCoreAsync<T>(string queue, string exchange, IReadOnlyList<string> routingKeys,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken)
        where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(handler);

        var exchangeType = await _factory.DetectExchangeTypeAsync(exchange, cancellationToken);

        if (exchangeType == AwsSqsBaseService.ExchangeType.Topic) {
            var topicArn = await _adminHelper.EnsureTopicExistsAsync(exchange, cancellationToken);
            await _adminHelper.EnsureQueueExistsAsync(queue, true, cancellationToken);
            var queueArn = await _adminHelper.GetQueueArnAsync(queue, cancellationToken);

            string? filterPolicy = null;
            if (routingKeys.Count > 0 && !(routingKeys.Count == 1 && routingKeys[0] is "#" or "*"))
                filterPolicy = JsonSerializer.Serialize(new { RoutingKey = routingKeys });

            await _adminHelper.SubscribeQueueToTopicAsync(queueArn, topicArn, filterPolicy, cancellationToken);
            _logger.LogDebug("Fila '{Queue}' inscrita no tópico '{Exchange}'.", queue, exchange);
        }
        else {
            await _adminHelper.EnsureQueueExistsAsync(queue, true, cancellationToken);
        }

        var primaryKey = routingKeys.FirstOrDefault() ?? string.Empty;
        await _consumerManager.StartConsumerAsync(queue, exchange, primaryKey, handler, retryOptions,
            cancellationToken);
    }

    private async Task PublishBodyAsync(string exchange, string routingKey, string body, string contentType,
        string correlationId, CancellationToken ct) {
        var exchangeType = await _factory.DetectExchangeTypeAsync(exchange, ct);

        if (exchangeType == AwsSqsBaseService.ExchangeType.Topic)
            await PublishToSnsAsync(exchange, routingKey, body, contentType, correlationId, ct);
        else
            await PublishToSqsAsync(exchange, body, contentType, correlationId, ct);

        _logger.LogDebug("Mensagem publicada em {ExchangeType} '{Exchange}'.", exchangeType, exchange);
    }

    private async Task PublishToSnsAsync(string topicName, string routingKey, string body, string contentType,
        string correlationId, CancellationToken ct) {
        var topicArn = await _adminHelper.EnsureTopicExistsAsync(topicName, ct);
        var snsClient = await _factory.GetSnsClientAsync(ct);

        var request = new PublishRequest {
            TopicArn = topicArn,
            Message = body,
            Subject = string.IsNullOrWhiteSpace(routingKey) ? null : routingKey,
            MessageAttributes = new Dictionary<string, SnsMessageAttributeValue> {
                ["ContentType"] = new() { DataType = "String", StringValue = contentType },
                ["RoutingKey"] = new() { DataType = "String", StringValue = routingKey ?? string.Empty },
                ["CorrelationId"] = new() { DataType = "String", StringValue = correlationId }
            }
        };

        if (_factory.IsFifoQueue(topicName)) {
            request.MessageGroupId = string.IsNullOrWhiteSpace(routingKey) ? "default" : routingKey;
            request.MessageDeduplicationId = correlationId;
        }

        await snsClient.PublishAsync(request, ct);
    }

    private async Task PublishToSqsAsync(string queueName, string body, string contentType, string correlationId,
        CancellationToken ct) {
        var queueUrl = await _adminHelper.EnsureQueueExistsAsync(queueName, false, ct);
        var sqsClient = await _factory.GetSqsClientAsync(ct);

        var request = new SendMessageRequest {
            QueueUrl = queueUrl,
            MessageBody = body,
            MessageAttributes = new Dictionary<string, SqsMessageAttributeValue> {
                ["ContentType"] = new() { DataType = "String", StringValue = contentType },
                ["CorrelationId"] = new() { DataType = "String", StringValue = correlationId }
            }
        };

        if (_factory.IsFifoQueue(queueName)) {
            request.MessageGroupId = "default";
            request.MessageDeduplicationId = correlationId;
        }

        await sqsClient.SendMessageAsync(request, ct);
    }
}
