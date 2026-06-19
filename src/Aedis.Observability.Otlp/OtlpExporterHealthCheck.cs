using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Aedis.Observability.Otlp;

/// <summary>
///     Health check de <em>readiness</em> que verifica a acessibilidade do endpoint OTLP. Reporta
///     <c>Healthy</c> quando o OTLP não está configurado (telemetria é opcional/best-effort).
/// </summary>
public sealed class OtlpExporterHealthCheck(IOptions<TelemetryOptions> options, IHttpClientFactory httpClientFactory)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        var endpoint = options.Value.OtlpEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            return HealthCheckResult.Healthy("OTLP não configurado.");

        try {
            var client = httpClientFactory.CreateClient("otlp-health");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent([]) };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return HealthCheckResult.Healthy($"Endpoint OTLP acessível (HTTP {(int)response.StatusCode}).");
        }
        catch (TaskCanceledException ex) {
            return HealthCheckResult.Unhealthy("Timeout no endpoint OTLP.", ex);
        }
        catch (HttpRequestException ex) {
            return HealthCheckResult.Unhealthy($"Endpoint OTLP inacessível: {ex.Message}", ex);
        }
    }
}
