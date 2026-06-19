using Aedis.Diagnostics;
using Aedis.Hosting.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Diagnósticos zero-config do Aedis: registra os health checks de processo (<c>self</c> e
///     <c>uptime</c> como <c>live</c>) e o de desligamento gracioso (<c>shutdown</c> como <c>ready</c>),
///     além do <see cref="IDisposableRegistry" /> e do orquestrador de desligamento gracioso que descarta
///     os recursos registrados (locks de liderança, etc.) ao receber o sinal de parada.
///     Health checks de dependências (broker, cache, banco) se registram com a tag <c>ready</c> nas suas
///     próprias extensões. Mapeie os endpoints com <c>MapAedisHealthChecks()</c>.
/// </summary>
public static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>
    ///     Registra os diagnósticos do Aedis no contêiner: health checks de processo, o
    ///     <see cref="IDisposableRegistry" /> e o serviço de desligamento gracioso. Chame uma vez na
    ///     composição da aplicação; use <paramref name="configure" /> para ajustar o
    ///     <see cref="GracefulShutdownOptions" /> (ex.: o atraso de drenagem).
    /// </summary>
    /// <param name="services">Coleção de serviços a configurar.</param>
    /// <param name="configure">Configuração opcional das opções de desligamento gracioso.</param>
    /// <returns>A mesma <paramref name="services" />, para encadeamento.</returns>
    public static IServiceCollection AddAedisDiagnostics(this IServiceCollection services,
        Action<GracefulShutdownOptions>? configure = null) {
        services.TryAddSingleton<ShutdownHealthCheck>();
        services.TryAddSingleton<IDisposableRegistry, DisposableRegistry>();

        var options = services.AddOptions<GracefulShutdownOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.AddHostedService<GracefulShutdownHostedService>();

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            .AddCheck<UptimeHealthCheck>("uptime", tags: ["live"])
            .AddCheck<ShutdownHealthCheck>("shutdown", tags: ["ready"]);

        return services;
    }
}
