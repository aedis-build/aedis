using Aedis.Diagnostics;
using Aedis.Hosting.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
    ///     <see cref="GracefulShutdownOptions" /> (atraso de drenagem e tempo máximo de desligamento). O
    ///     <see cref="GracefulShutdownOptions.ShutdownTimeout" /> é propagado para o
    ///     <see cref="HostOptions.ShutdownTimeout" />, para o host não abortar no meio da drenagem.
    /// </summary>
    /// <param name="services">Coleção de serviços a configurar.</param>
    /// <param name="configure">Configuração opcional das opções de desligamento gracioso.</param>
    /// <returns>A mesma <paramref name="services" />, para encadeamento.</returns>
    public static IServiceCollection AddAedisDiagnostics(this IServiceCollection services,
        Action<GracefulShutdownOptions>? configure = null) {
        services.TryAddSingleton<ShutdownHealthCheck>();
        services.TryAddSingleton<IDisposableRegistry, DisposableRegistry>();

        var options = services.AddOptions<GracefulShutdownOptions>()
            .Validate(graceful => graceful.ShutdownTimeout >= graceful.DrainDelay,
                "GracefulShutdownOptions.ShutdownTimeout deve ser maior ou igual a DrainDelay; caso contrário o host aborta o desligamento antes de a drenagem terminar.")
            .ValidateOnStart();
        if (configure is not null)
            options.Configure(configure);

        services.AddOptions<HostOptions>()
            .Configure<IOptions<GracefulShutdownOptions>>(
                (host, graceful) => host.ShutdownTimeout = graceful.Value.ShutdownTimeout);

        services.AddHostedService<GracefulShutdownHostedService>();

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            .AddCheck<UptimeHealthCheck>("uptime", tags: ["live"])
            .AddCheck<ShutdownHealthCheck>("shutdown", tags: ["ready"]);

        return services;
    }
}
