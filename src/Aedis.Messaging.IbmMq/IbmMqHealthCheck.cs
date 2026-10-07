using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Health check do IBM MQ que empresta e devolve uma conexão do pool de publicação do broker (não abre
///     conexões fora do teto). Queue Manager inacessível reporta <c>Degraded</c>, não <c>Unhealthy</c>: o
///     consumer religa sozinho quando o QM volta, e derrubar a réplica só adicionaria churn ao incidente.
/// </summary>
public sealed class IbmMqHealthCheck : IHealthCheck
{
    private readonly IbmMqMessageBrokerService _broker;
    private readonly ILogger<IbmMqHealthCheck> _logger;
    private readonly IbmMqOptions _options;

    /// <summary>Cria o health check ligado ao broker IBM MQ, cujo pool de publicação é reusado nas verificações.</summary>
    public IbmMqHealthCheck(IOptions<IbmMqOptions> options, ILogger<IbmMqHealthCheck> logger,
        IbmMqMessageBrokerService broker) {
        _options = options.Value;
        _logger = logger;
        _broker = broker;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        try {
            var connected = await _broker.TryConnectAsync(cancellationToken);

            return connected
                ? HealthCheckResult.Healthy($"Conexão IBM MQ saudável para {_options.QueueManager}.")
                : HealthCheckResult.Degraded($"IBM MQ indisponível para {_options.QueueManager}.");
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Falha no health check do IBM MQ para o QueueManager {QueueManager}.", _options.QueueManager);
            return HealthCheckResult.Degraded($"IBM MQ indisponível para {_options.QueueManager}.", ex);
        }
    }
}
