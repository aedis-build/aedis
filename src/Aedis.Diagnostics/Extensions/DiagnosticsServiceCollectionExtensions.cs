using Aedis.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Diagnósticos zero-config do Aedis: registra os health checks de processo (<c>self</c> e
///     <c>uptime</c> como <c>live</c>) e o de desligamento gracioso (<c>shutdown</c> como <c>ready</c>).
///     Health checks de dependências (broker, cache, banco) se registram com a tag <c>ready</c> nas suas
///     próprias extensões. Mapeie os endpoints com <c>MapAedisHealthChecks()</c>.
/// </summary>
public static class DiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddAedisDiagnostics(this IServiceCollection services) {
        services.TryAddSingleton<ShutdownHealthCheck>();
        services.AddHostedService<ShutdownSignalHostedService>();

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            .AddCheck<UptimeHealthCheck>("uptime", tags: ["live"])
            .AddCheck<ShutdownHealthCheck>("shutdown", tags: ["ready"]);

        return services;
    }

    /// <summary>Liga o sinal de desligamento da aplicação ao <see cref="ShutdownHealthCheck" />.</summary>
    private sealed class ShutdownSignalHostedService(ShutdownHealthCheck healthCheck, IHostApplicationLifetime lifetime)
        : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) {
            lifetime.ApplicationStopping.Register(healthCheck.MarkAsShuttingDown);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
