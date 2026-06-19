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

    /// <summary>Cria o helper de administração com um cliente apontando para a connection string configurada.</summary>
    public ServiceBusAdministrationHelper(IOptions<ServiceBusOptions> options,
        ILogger<ServiceBusAdministrationHelper> logger) {
        _adminClient = new ServiceBusAdministrationClient(options.Value.ConnectionString);
        _logger = logger;
    }

    /// <summary>
    ///     Garante a existência da fila (idempotente): cria com TTL, lock duration e MaxDeliveryCount
    ///     padrão se ainda não existir. O nome é normalizado para as convenções do Service Bus.
    /// </summary>
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

    /// <summary>
    ///     Garante a existência do tópico (idempotente): cria com TTL padrão e sem particionamento se ainda
    ///     não existir. O nome é normalizado para as convenções do Service Bus.
    /// </summary>
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

    /// <summary>
    ///     Garante a existência da subscription no tópico (idempotente). Quando <paramref name="filter" /> é
    ///     informado, cria uma regra SQL <c>Subject = '{filter}'</c> para rotear apenas mensagens com aquele
    ///     routing key (carregado no <c>Subject</c> da mensagem).
    /// </summary>
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
