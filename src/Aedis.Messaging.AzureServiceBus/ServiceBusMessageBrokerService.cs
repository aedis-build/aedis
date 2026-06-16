using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AzureServiceBus;

/// <summary>
///     Broker Azure Service Bus do Aedis. Trata exchange não vazio como Topic (pub/sub) e vazio como
///     Queue (point-to-point), auto-provisionando os recursos. O payload é serializado via
///     <see cref="MessageSerializerResolver" />; o routing key vai no <c>Subject</c> da mensagem, casando
///     com a regra de filtro da subscription.
/// </summary>
public sealed class ServiceBusMessageBrokerService : ServiceBusBaseService, IMessageBrokerService
{
    private readonly ServiceBusAdministrationHelper? _adminHelper;
    private readonly ServiceBusConsumerManager _consumerManager;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly ILogger<ServiceBusMessageBrokerService> _logger;
    private readonly MessageSerializerResolver _serializers;
    private int _consecutiveUnhealthy;

    public ServiceBusMessageBrokerService(IOptions<ServiceBusOptions> options,
        ILogger<ServiceBusMessageBrokerService> logger, ServiceBusAdministrationHelper? adminHelper = null,
        IHostApplicationLifetime? lifetime = null, MessageSerializerResolver? serializers = null,
        ILoggerFactory? loggerFactory = null)
        : base(options, logger) {
        _logger = logger;
        _adminHelper = adminHelper;
        _lifetime = lifetime;
        _serializers = serializers ?? MessageSerializerResolver.CreateDefault();
        var consumerLogger = loggerFactory?.CreateLogger<ServiceBusConsumerManager>() ?? (ILogger)logger;
        _consumerManager = new ServiceBusConsumerManager(consumerLogger, options, _serializers);
    }

    public async Task PublishAsync<T>(string exchange, string routingKey, T message,
        CancellationToken cancellationToken = default) where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(message);

        var data = message.ToData();
        var serializer = _serializers.ResolveForSerialize(data);
        await SendAsync(exchange, routingKey, serializer.Serialize(data), serializer.ContentType,
            message.CorrelationId, cancellationToken);
    }

    public async Task PublishRawAsync(string exchange, string routingKey, ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream", string? correlationId = null,
        CancellationToken cancellationToken = default) {
        await SendAsync(exchange, routingKey, payload, contentType, correlationId ?? Guid.NewGuid().ToString(),
            cancellationToken);
    }

    public async Task SubscribeAsync<T>(string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(retryOptions);

        var client = await GetClientAsync();

        if (IsTopic(exchange)) {
            if (_adminHelper != null) {
                await _adminHelper.EnsureTopicExistsAsync(exchange, cancellationToken);
                await _adminHelper.EnsureSubscriptionExistsAsync(exchange, queue, routingKey, cancellationToken);
            }
        }
        else if (_adminHelper != null) {
            await _adminHelper.EnsureQueueExistsAsync(queue, cancellationToken);
        }

        var consumerId = await _consumerManager.StartConsumerAsync(client, queue, exchange, routingKey, handler,
            retryOptions, cancellationToken);

        await KeepConsumerAliveAsync(consumerId, cancellationToken);
    }

    public override async ValueTask DisposeAsync() {
        await _consumerManager.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task SendAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, string contentType,
        string correlationId, CancellationToken cancellationToken) {
        var client = await GetClientAsync();

        string entity;
        if (IsTopic(exchange)) {
            entity = NormalizeName(exchange);
            if (_adminHelper != null) await _adminHelper.EnsureTopicExistsAsync(entity, cancellationToken);
        }
        else {
            entity = NormalizeName(routingKey);
            if (_adminHelper != null) await _adminHelper.EnsureQueueExistsAsync(entity, cancellationToken);
        }

        await using var sender = client.CreateSender(entity);

        var serviceBusMessage = new ServiceBusMessage(body) {
            ContentType = contentType,
            MessageId = correlationId,
            Subject = routingKey
        };
        if (!string.IsNullOrEmpty(correlationId))
            serviceBusMessage.CorrelationId = correlationId;

        await sender.SendMessageAsync(serviceBusMessage, cancellationToken);
        _logger.LogDebug("Mensagem publicada em '{Entity}' ({ContentType}).", entity, contentType);
    }

    private async Task KeepConsumerAliveAsync(string consumerId, CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested) {
                if (!await _consumerManager.IsConsumerHealthyAsync(consumerId)) {
                    _logger.LogWarning("Consumer {ConsumerId} não está saudável — tentando reiniciar.", consumerId);
                    await _consumerManager.RestartUnhealthyConsumersAsync(cancellationToken);

                    if (++_consecutiveUnhealthy >= 5) {
                        _logger.LogError("Consumer {ConsumerId} insalubre por tempo prolongado — encerrando a aplicação.",
                            consumerId);
                        _lifetime?.StopApplication();
                    }
                }
                else {
                    _consecutiveUnhealthy = 0;
                }

                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            }
        }
        catch (OperationCanceledException) {
            _logger.LogDebug("Encerramento solicitado para o consumer {ConsumerId}.", consumerId);
        }
        finally {
            await _consumerManager.StopConsumerAsync(consumerId, CancellationToken.None);
        }
    }
}
