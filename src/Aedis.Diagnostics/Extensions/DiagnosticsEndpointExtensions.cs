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
    public static IEndpointRouteBuilder MapAedisHealthChecks(this IEndpointRouteBuilder endpoints) {
        endpoints.MapHealthChecks("/health", JsonOptions());
        endpoints.MapHealthChecks("/health/live", JsonOptions(r => r.Tags.Contains("live")));
        endpoints.MapHealthChecks("/health/ready", JsonOptions(r => r.Tags.Contains("ready")));
        return endpoints;
    }

    private static HealthCheckOptions JsonOptions(Func<HealthCheckRegistration, bool>? predicate = null) {
        return new HealthCheckOptions {
            Predicate = predicate ?? (_ => true),
            ResponseWriter = async (context, report) => {
                context.Response.ContentType = "application/json";

                var result = new {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(e => new {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        data = e.Value.Data
                    })
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(result, SystemJsonOptionsFactory.Create()));
            }
        };
    }
}
