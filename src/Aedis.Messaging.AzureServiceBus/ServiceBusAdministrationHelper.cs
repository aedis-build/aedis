using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AzureServiceBus;

/// <summary>
///     Auto-provisiona os recursos do Azure Service Bus: cria filas, tópicos e subscriptions (com regra
///     de filtro <c>Subject = '{routingKey}'</c> para roteamento) quando ainda não existem. Operações
///     idempotentes.
/// </summary>
public sealed class ServiceBusAdministrationHelper
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    private const int MaxDeliveryCount = 10;

    private readonly ServiceBusAdministrationClient _adminClient;
    private readonly ILogger<ServiceBusAdministrationHelper> _logger;

    public ServiceBusAdministrationHelper(IOptions<ServiceBusOptions> options,
        ILogger<ServiceBusAdministrationHelper> logger) {
        _adminClient = new ServiceBusAdministrationClient(options.Value.ConnectionString);
        _logger = logger;
    }

    public async Task EnsureQueueExistsAsync(string queueName, CancellationToken cancellationToken = default) {
        var name = ServiceBusBaseService.NormalizeName(queueName);
        if (await _adminClient.QueueExistsAsync(name, cancellationToken))
            return;

        await _adminClient.CreateQueueAsync(new CreateQueueOptions(name) {
            DefaultMessageTimeToLive = DefaultTtl,
            MaxDeliveryCount = MaxDeliveryCount,
            LockDuration = LockDuration
        }, cancellationToken);
        _logger.LogDebug("Fila '{QueueName}' criada.", name);
    }

    public async Task EnsureTopicExistsAsync(string topicName, CancellationToken cancellationToken = default) {
        var name = ServiceBusBaseService.NormalizeName(topicName);
        if (await _adminClient.TopicExistsAsync(name, cancellationToken))
            return;

        await _adminClient.CreateTopicAsync(new CreateTopicOptions(name) {
            DefaultMessageTimeToLive = DefaultTtl,
            EnablePartitioning = false
        }, cancellationToken);
        _logger.LogDebug("Tópico '{TopicName}' criado.", name);
    }

    public async Task EnsureSubscriptionExistsAsync(string topicName, string subscriptionName, string? filter = null,
        CancellationToken cancellationToken = default) {
        var topic = ServiceBusBaseService.NormalizeName(topicName);
        var subscription = ServiceBusBaseService.NormalizeName(subscriptionName);

        if (await _adminClient.SubscriptionExistsAsync(topic, subscription, cancellationToken))
            return;

        await _adminClient.CreateSubscriptionAsync(new CreateSubscriptionOptions(topic, subscription) {
            DefaultMessageTimeToLive = DefaultTtl,
            MaxDeliveryCount = MaxDeliveryCount,
            LockDuration = LockDuration
        }, cancellationToken);

        if (!string.IsNullOrWhiteSpace(filter)) {
            await _adminClient.CreateRuleAsync(topic, subscription, new CreateRuleOptions("DefaultRule") {
                Filter = new SqlRuleFilter($"Subject = '{filter}'")
            }, cancellationToken);
            _logger.LogDebug("Subscription '{Subscription}' criada no tópico '{Topic}' com filtro '{Filter}'.",
                subscription, topic, filter);
        }
        else {
            _logger.LogDebug("Subscription '{Subscription}' criada no tópico '{Topic}'.", subscription, topic);
        }
    }
}
