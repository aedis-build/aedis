using System.Text.Json;
using Aedis.Core.Utils;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Mapeia os endpoints de health check padrão do Aedis.
/// </summary>
public static class DiagnosticsEndpointExtensions
{
    /// <summary>
    ///     Mapeia <c>/health</c> (todos), <c>/health/live</c> (liveness — tag <c>live</c>) e
    ///     <c>/health/ready</c> (readiness — tag <c>ready</c>), com resposta JSON.
    /// </summary>
    /// <remarks>
    ///     <c>/health/live</c> e <c>/health/ready</c> são anônimos — o kubelet não envia token — e respondem só
    ///     status e nome/status de cada check, sem <c>description</c>/<c>data</c>. <c>/health</c> mantém o
    ///     detalhe completo e segue a política de autorização do host (no <c>AedisApiHost</c>, a fallback
    ///     autenticada), para não expor detalhe interno a anônimos.
    /// </remarks>
    public static IEndpointRouteBuilder MapAedisHealthChecks(this IEndpointRouteBuilder endpoints) {
        endpoints.MapHealthChecks("/health", JsonOptions(detailed: true));
        endpoints.MapHealthChecks("/health/live", JsonOptions(detailed: false, r => r.Tags.Contains("live"))).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", JsonOptions(detailed: false, r => r.Tags.Contains("ready"))).AllowAnonymous();
        return endpoints;
    }

    private static HealthCheckOptions JsonOptions(bool detailed, Func<HealthCheckRegistration, bool>? predicate = null) {
        return new HealthCheckOptions {
            Predicate = predicate ?? (_ => true),
            ResponseWriter = async (context, report) => {
                context.Response.ContentType = "application/json";

                var result = new {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(e => detailed
                        ? (object)new {
                            name = e.Key,
                            status = e.Value.Status.ToString(),
                            description = e.Value.Description,
                            data = e.Value.Data
                        }
                        : new { name = e.Key, status = e.Value.Status.ToString() })
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(result, SystemJsonOptionsFactory.Create()));
            }
        };
    }
}
