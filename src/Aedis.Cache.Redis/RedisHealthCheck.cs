using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Cache.Redis;

/// <summary>
///     Health check de <em>readiness</em> do Redis que reutiliza a conexão do <see cref="RedisCache" />
///     (não abre conexões novas). Reporta <c>Degraded</c> quando o PING passa de 1s e <c>Unhealthy</c>
///     quando a conexão falha.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly RedisCache _cache;
    private readonly ILogger<RedisHealthCheck> _logger;
    private readonly RedisCacheOptions _options;

    public RedisHealthCheck(IOptions<RedisCacheOptions> options, ILogger<RedisHealthCheck> logger, RedisCache cache) {
        _options = options.Value;
        _logger = logger;
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        try {
            _logger.LogDebug("Verificando a saúde da conexão Redis para o endpoint {EndPoint}.", _options.EndPoint);

            var pingResult = await _cache.Database.PingAsync().ConfigureAwait(false);

            if (pingResult.TotalMilliseconds > 1000) {
                _logger.LogWarning("Conexão Redis lenta para o endpoint {EndPoint}. Latência: {Latency}ms.",
                    _options.EndPoint, pingResult.TotalMilliseconds);
                return HealthCheckResult.Degraded(
                    $"Conexão Redis lenta para {_options.EndPoint}. Latência: {pingResult.TotalMilliseconds}ms.");
            }

            return HealthCheckResult.Healthy($"Conexão Redis saudável para {_options.EndPoint}.");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Falha na verificação de saúde do Redis para o endpoint {EndPoint}.",
                _options.EndPoint);
            return HealthCheckResult.Unhealthy($"Conexão Redis não saudável para {_options.EndPoint}.", ex);
        }
    }
}
