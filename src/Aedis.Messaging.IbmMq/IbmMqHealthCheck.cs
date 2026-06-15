using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Health check de <em>readiness</em> do IBM MQ que reusa a conexão do broker (não abre conexões
///     novas). Reporta <c>Healthy</c> quando o Queue Manager está acessível e <c>Unhealthy</c> quando a
///     conexão não pode ser estabelecida ou validada.
/// </summary>
public sealed class IbmMqHealthCheck : IHealthCheck
{
    private readonly IbmMqMessageBrokerService _broker;
    private readonly ILogger<IbmMqHealthCheck> _logger;
    private readonly IbmMqOptions _options;

    public IbmMqHealthCheck(IOptions<IbmMqOptions> options, ILogger<IbmMqHealthCheck> logger,
        IbmMqMessageBrokerService broker) {
        _options = options.Value;
        _logger = logger;
        _broker = broker;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        try {
            _logger.LogDebug("Verificando a saúde da conexão IBM MQ para o QueueManager {QueueManager}.",
                _options.QueueManager);

            await _broker.EnsureConnectionAsync();
            var healthy = await _broker.IsConnectionHealthyAsync();

            return healthy
                ? HealthCheckResult.Healthy($"Conexão IBM MQ saudável para {_options.QueueManager}.")
                : HealthCheckResult.Unhealthy($"Conexão IBM MQ indisponível para {_options.QueueManager}.");
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Falha no health check do IBM MQ para o QueueManager {QueueManager}.",
                _options.QueueManager);
            return HealthCheckResult.Unhealthy($"Conexão IBM MQ indisponível para {_options.QueueManager}.", ex);
        }
    }
}
