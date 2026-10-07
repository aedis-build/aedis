using System.Text.Json;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Aedis.Messaging.Telemetry;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using SnsMessageAttributeValue = Amazon.SimpleNotificationService.Model.MessageAttributeValue;
using SqsMessageAttributeValue = Amazon.SQS.Model.MessageAttributeValue;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Broker AWS SQS/SNS do Aedis. Resolve se o exchange é um SNS Topic (pub/sub) ou uma SQS Queue
///     (point-to-point) e auto-provisiona os recursos no subscribe. O payload é serializado via
///     <see cref="MessageSerializerResolver" />, opcionalmente comprimido e codificado em base64 (o corpo do
///     SNS/SQS é texto); content-type, encodings, correlação e <c>traceparent</c>/<c>tracestate</c> viajam
///     como atributos de mensagem. Cada publish gera span e métricas via <see cref="MessagingInstrumentation" />.
/// </summary>
public sealed class AwsSqsMessageBrokerService : IMessageBrokerService
{
    private readonly AwsSqsAdministrationHelper _adminHelper;
    private readonly AwsSqsConsumerManager _consumerManager;
    private readonly MessageEncoderResolver _encoders;
    private readonly IAwsPubSubFactory _factory;
    private readonly ILogger<AwsSqsMessageBrokerService> _logger;
    private readonly MessageSerializerResolver _serializers;

    /// <summary>
    ///     Cria o broker AWS SQS/SNS com a factory de clientes compartilhada, o admin helper de
    ///     auto-provisionamento, o gerenciador de consumidores, o resolvedor de serializadores e o resolvedor
    ///     de content-encoding (usam o default quando ausentes).
    /// </summary>
    public AwsSqsMessageBrokerService(IAwsPubSubFactory factory, ILogger<AwsSqsMessageBrokerService> logger,
        AwsSqsAdministrationHelper adminHelper, AwsSqsConsumerManager consumerManager,
        MessageSerializerResolver? serializers = null, MessageEncoderResolver? encoders = null) {
        _factory = factory;
        _logger = logger;
        _adminHelper = adminHelper;
        _consumerManager = consumerManager;
        _serializers = serializers ?? MessageSerializerResolver.CreateDefault();
        _encoders = encoders ?? MessageEncoderResolver.CreateDefault();
    }

    /// <summary>
    ///     Publica uma mensagem tipada no exchange (SNS Topic ou SQS Queue), serializando o payload,
    ///     comprimindo quando a política manda e codificando em base64, com o contexto de trace nos atributos.
    /// </summary>
    public async Task PublishAsync<T>(string exchange, string routingKey, T message,
        CancellationToken cancellationToken = default) where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(message);

        using var operation = MessagingInstrumentation.StartPublish(AwsSqsConsumerManager.SystemName, exchange, message, _logger);
        var traceHeaders = new Dictionary<string, string>();
        MessagingInstrumentation.InjectContext(operation.Activity, (key, value) => traceHeaders[key] = value);

        try {
            var data = message.ToData();
            var serializer = _serializers.ResolveForSerialize(data);
            var serialized = serializer.Serialize(data);
            var encoder = _encoders.ResolveForEncode(serialized.Length);
            var body = Convert.ToBase64String(encoder.Encode(serialized).ToArray());

            await PublishBodyAsync(exchange, routingKey, body, serializer.ContentType, encoder.Encoding,
                message.CorrelationId, traceHeaders, cancellationToken);
        }
        catch (Exception ex) {
            operation.Complete(MessageOutcome.UnhandledFailure, ex);
            throw;
        }
    }

    /// <summary>
    ///     Publica um payload bruto (bytes) no exchange, codificado em base64, sem passar por serializador.
    ///     Gera um CorrelationId quando não informado.
    /// </summary>
    public async Task PublishRawAsync(string exchange, string routingKey, ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream", string? correlationId = null,
        CancellationToken cancellationToken = default) {
        var traceHeaders = new Dictionary<string, string>();
        MessagingInstrumentation.InjectContext(null, (key, value) => traceHeaders[key] = value);

        var encoder = _encoders.ResolveForEncode(payload.Length);
        var body = Convert.ToBase64String(encoder.Encode(payload).ToArray());
        await PublishBodyAsync(exchange, routingKey, body, contentType, encoder.Encoding,
            correlationId ?? Guid.NewGuid().ToString(), traceHeaders, cancellationToken);
    }

    /// <summary>
    ///     Assina o exchange com uma routing key, auto-provisionando os recursos (tópico, fila, DLQ e
    ///     inscrição) e iniciando o consumer. Routing key vazia, <c>#</c> ou <c>*</c> recebe tudo.
    /// </summary>
    public Task SubscribeAsync<T>(string queue, string exchange, string routingKey, IMessageHandler<T> handler,
        ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default) where T : class, IMessage =>
        SubscribeCoreAsync(queue, exchange,
            string.IsNullOrWhiteSpace(routingKey) ? [] : [routingKey], handler, retryOptions, cancellationToken);

    /// <summary>
    ///     Assina o exchange com várias routing keys (vira filter policy OR na inscrição SNS),
    ///     auto-provisionando os recursos e iniciando o consumer. Keys vazias são ignoradas.
    /// </summary>
    public Task SubscribeAsync<T>(string queue, string exchange, IEnumerable<string> routingKeys,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage =>
        SubscribeCoreAsync(queue, exchange, routingKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),
            handler, retryOptions, cancellationToken);

    private async Task SubscribeCoreAsync<T>(string queue, string exchange, IReadOnlyList<string> routingKeys,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken)
        where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(retryOptions);

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
        await _consumerManager.StartConsumerAsync(queue, exchange, primaryKey, handler, retryOptions, cancellationToken);
    }

    private async Task PublishBodyAsync(string exchange, string routingKey, string body, string contentType,
        string contentEncoding, string correlationId, IReadOnlyDictionary<string, string> traceHeaders,
        CancellationToken ct) {
        var exchangeType = await _factory.DetectExchangeTypeAsync(exchange, ct);

        if (exchangeType == AwsSqsBaseService.ExchangeType.Topic)
            await PublishToSnsAsync(exchange, routingKey, body, contentType, contentEncoding, correlationId, traceHeaders, ct);
        else
            await PublishToSqsAsync(exchange, body, contentType, contentEncoding, correlationId, traceHeaders, ct);

        _logger.LogDebug("Mensagem publicada em {ExchangeType} '{Exchange}' (encoding {Encoding}).",
            exchangeType, exchange, contentEncoding);
    }

    private async Task PublishToSnsAsync(string topicName, string routingKey, string body, string contentType,
        string contentEncoding, string correlationId, IReadOnlyDictionary<string, string> traceHeaders,
        CancellationToken ct) {
        var topicArn = await _adminHelper.EnsureTopicExistsAsync(topicName, ct);
        var snsClient = await _factory.GetSnsClientAsync(ct);

        var attributes = new Dictionary<string, SnsMessageAttributeValue> {
            ["Content-Type"] = new() { DataType = "String", StringValue = contentType },
            ["Content-Encoding"] = new() { DataType = "String", StringValue = contentEncoding },
            ["Content-Transfer-Encoding"] = new() { DataType = "String", StringValue = "base64" },
            ["CorrelationId"] = new() { DataType = "String", StringValue = correlationId }
        };

        if (!string.IsNullOrWhiteSpace(routingKey))
            attributes["RoutingKey"] = new SnsMessageAttributeValue { DataType = "String", StringValue = routingKey };

        foreach (var (key, value) in traceHeaders)
            attributes[key] = new SnsMessageAttributeValue { DataType = "String", StringValue = value };

        var request = new PublishRequest {
            TopicArn = topicArn,
            Message = body,
            Subject = string.IsNullOrWhiteSpace(routingKey) ? null : routingKey,
            MessageAttributes = attributes
        };

        if (_factory.IsFifoQueue(_factory.ResolveQueueName(topicName))) {
            request.MessageGroupId = string.IsNullOrWhiteSpace(routingKey) ? "default" : routingKey;
            request.MessageDeduplicationId = correlationId;
        }

        await snsClient.PublishAsync(request, ct);
    }

    private async Task PublishToSqsAsync(string queueName, string body, string contentType, string contentEncoding,
        string correlationId, IReadOnlyDictionary<string, string> traceHeaders, CancellationToken ct) {
        var queueUrl = await _adminHelper.EnsureQueueExistsAsync(queueName, false, ct);
        var sqsClient = await _factory.GetSqsClientAsync(ct);

        var attributes = new Dictionary<string, SqsMessageAttributeValue> {
            ["Content-Type"] = new() { DataType = "String", StringValue = contentType },
            ["Content-Encoding"] = new() { DataType = "String", StringValue = contentEncoding },
            ["Content-Transfer-Encoding"] = new() { DataType = "String", StringValue = "base64" },
            ["CorrelationId"] = new() { DataType = "String", StringValue = correlationId }
        };

        foreach (var (key, value) in traceHeaders)
            attributes[key] = new SqsMessageAttributeValue { DataType = "String", StringValue = value };

        var request = new SendMessageRequest {
            QueueUrl = queueUrl,
            MessageBody = body,
            MessageAttributes = attributes
        };

        if (_factory.IsFifoQueue(_factory.ResolveQueueName(queueName))) {
            request.MessageGroupId = "default";
            request.MessageDeduplicationId = correlationId;
        }

        await sqsClient.SendMessageAsync(request, ct);
    }
}
