using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aedis.Diagnostics;

/// <summary>
///     Health check de <em>liveness</em> que reporta o uptime da aplicação — útil para detectar
///     reinícios frequentes (ex.: crash loop) em orquestradores como o Kubernetes.
/// </summary>
public sealed class UptimeHealthCheck : IHealthCheck
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        var uptime = DateTimeOffset.UtcNow - _startTime;

        var data = new Dictionary<string, object> {
            ["uptime"] = uptime.ToString(),
            ["uptime_seconds"] = uptime.TotalSeconds,
            ["started_at_utc"] = _startTime.ToString("o"),
            ["current_time_utc"] = DateTimeOffset.UtcNow.ToString("o")
        };

        return Task.FromResult(HealthCheckResult.Healthy($"Uptime: {uptime.TotalSeconds:F0}s", data));
    }
}
