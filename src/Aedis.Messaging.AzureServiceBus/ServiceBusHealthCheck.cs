using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Aedis.Messaging.AzureServiceBus;

/// <summary>
///     Health check de <em>readiness</em> do Azure Service Bus — reusa o cliente do broker e reporta
///     <c>Unhealthy</c> quando o cliente não pode ser obtido ou está fechado.
/// </summary>
public sealed class ServiceBusHealthCheck(
    ILogger<ServiceBusHealthCheck> logger,
    ServiceBusMessageBrokerService broker)
    : IHealthCheck
{
    /// <summary>Reporta <c>Healthy</c> se o cliente do broker está aberto; senão <c>Unhealthy</c>.</summary>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        try {
            var client = await broker.GetClientAsync();
            if (client is null || client.IsClosed)
                return HealthCheckResult.Unhealthy("Conexão Azure Service Bus está fechada.");

            return HealthCheckResult.Healthy("Conexão Azure Service Bus saudável.");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Falha no health check do Azure Service Bus.");
            return HealthCheckResult.Unhealthy("Conexão Azure Service Bus indisponível.", ex);
        }
    }
}
