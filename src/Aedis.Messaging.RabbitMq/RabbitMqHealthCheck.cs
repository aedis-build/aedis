using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.RabbitMq;

/// <summary>
///     Health check do RabbitMQ que reutiliza a conexão existente do broker (evita conexões extras).
/// </summary>
public class RabbitMqHealthCheck : IHealthCheck
{
    private readonly ILogger<RabbitMqHealthCheck> _logger;
    private readonly RabbitMqMessageBrokerService _broker;
    private readonly RabbitMqOptions _options;

    public RabbitMqHealthCheck(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqHealthCheck> logger,
        RabbitMqMessageBrokerService broker) {
        _options = options.Value;
        _logger = logger;
        _broker = broker;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        try {
            if (!_broker.IsConnectionHealthy) {
                _logger.LogWarning("Conexão RabbitMQ não está saudável (Host: {Host}).", _options.Host);
                return HealthCheckResult.Unhealthy($"RabbitMQ unhealthy (Host: {_options.Host}).");
            }

            var connection = await _broker.GetConnectionAsync();
            if (connection is not { IsOpen: true }) {
                _logger.LogWarning("Conexão RabbitMQ está fechada (Host: {Host}).", _options.Host);
                return HealthCheckResult.Unhealthy($"RabbitMQ connection closed (Host: {_options.Host}).");
            }

            return HealthCheckResult.Healthy($"RabbitMQ healthy (Host: {_options.Host}).");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Falha no health check do RabbitMQ (Host: {Host}).", _options.Host);
            return HealthCheckResult.Unhealthy($"RabbitMQ unhealthy (Host: {_options.Host}).", ex);
        }
    }
}
