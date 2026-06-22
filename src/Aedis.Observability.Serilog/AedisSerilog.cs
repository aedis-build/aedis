using Aedis.Core.Utils;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Aedis.Observability.Serilog;

/// <summary>
///     Logging estruturado do Aedis via Serilog. <strong>Entrega garantida por desenho:</strong> escreve
///     SEMPRE no Console (stdout, JSON compacto) — a rede de segurança durável coletada pelo agente da
///     plataforma (Kubernetes/Datadog/etc.), que nunca se perde — e, quando há endpoint OTLP configurado,
///     adiciona o sink OTLP em <em>lote</em> (com fila em memória) para entrega direta ao backend. Mesmo se
///     o OTLP cair, nenhum log se perde (o stdout permanece). Opt-in do OTLP pela seção <c>Telemetry</c>.
/// </summary>
public static class AedisSerilog
{
    /// <summary>
    ///     Cria o <see cref="ILogger" /> raiz do Serilog (use no bootstrap do <c>Program.cs</c>:
    ///     <c>Log.Logger = AedisSerilog.CreateLogger(config)</c>).
    /// </summary>
    public static global::Serilog.Core.Logger CreateLogger(IConfiguration configuration) {
        var loggerConfiguration = new LoggerConfiguration();
        Configure(loggerConfiguration, configuration);
        return loggerConfiguration.CreateLogger();
    }

    /// <summary>
    ///     Aplica a configuração padrão do Aedis a um <see cref="LoggerConfiguration" /> existente: níveis
    ///     mínimos (com overrides de ruído do ASP.NET Core e os configurados na seção <c>Serilog:MinimumLevel</c>),
    ///     filtro de ruído (<c>/health</c>, <c>/favicon.ico</c>) e enriquecimento de cada evento.
    /// </summary>
    /// <remarks>
    ///     Estratégia de sinks por desenho de entrega durável: escreve SEMPRE no Console em JSON compacto
    ///     (<see cref="CompactJsonFormatter" />) no stdout — a rede de segurança coletada pelo agente da
    ///     plataforma, que nunca se perde — e adiciona o sink OTLP em lote apenas quando há endpoint
    ///     configurado. O enriquecimento <c>application</c> usa a mesma tag das métricas
    ///     (<see cref="ApplicationInfo.Name" />), permitindo filtrar logs e métricas pelo mesmo serviço.
    /// </remarks>
    public static void Configure(LoggerConfiguration loggerConfiguration, IConfiguration configuration) {
        loggerConfiguration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.HealthChecks", LogEventLevel.Warning);

        ApplyConfiguredLevels(loggerConfiguration, configuration);

        var redaction = RedactionOptions.FromConfiguration(configuration);

        loggerConfiguration
            .Filter.ByExcluding(IsNoise)
            .Destructure.With(new SensitiveDataDestructuringPolicy(redaction))
            .Enrich.With(new LogTypeEnricher("Application"))
            .Enrich.WithProperty("application", ApplicationInfo.Name)
            .Enrich.FromLogContext()
            .Enrich.With(new RedactionEnricher(redaction))
            .WriteTo.Console(new CompactJsonFormatter());

        ConfigureOpenTelemetrySink(loggerConfiguration, configuration);
    }

    private static void ApplyConfiguredLevels(LoggerConfiguration loggerConfiguration, IConfiguration configuration) {
        var section = configuration.GetSection("Serilog:MinimumLevel");
        if (!section.Exists()) return;

        var defaultLevel = section["Default"];
        if (!string.IsNullOrEmpty(defaultLevel) && Enum.TryParse<LogEventLevel>(defaultLevel, true, out var level))
            loggerConfiguration.MinimumLevel.Is(level);

        foreach (var entry in section.GetSection("Override").AsEnumerable())
            if (!string.IsNullOrEmpty(entry.Key) && !string.IsNullOrEmpty(entry.Value)
                && Enum.TryParse<LogEventLevel>(entry.Value, true, out var overrideLevel)) {
                var source = entry.Key[(entry.Key.LastIndexOf(':') + 1)..];
                loggerConfiguration.MinimumLevel.Override(source, overrideLevel);
            }
    }

    private static bool IsNoise(LogEvent logEvent) {
        if (!logEvent.Properties.TryGetValue("RequestPath", out var requestPath))
            return false;

        var path = requestPath.ToString().Trim('"');
        return path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Sink OTLP de logs — opt-in via <c>Telemetry:OtlpEndpoint</c> (em lote, para entrega ao backend).
    /// </summary>
    /// <remarks>
    ///     O lote com fila grande agrega eventos para throughput e absorve picos sem bloquear o produtor
    ///     nem perder eventos: agrega até 1.000 por lote, esvazia a cada 2s e tolera até 100.000 enfileirados.
    /// </remarks>
    private static void ConfigureOpenTelemetrySink(LoggerConfiguration loggerConfiguration,
        IConfiguration configuration) {
        var telemetry = configuration.GetSection("Telemetry");
        if (!telemetry.Exists()) return;

        var endpoint = telemetry["OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        if (bool.TryParse(telemetry["EnableLogs"], out var enableLogs) && !enableLogs) return;

        var apiKey = telemetry["OtlpApiKey"];

        loggerConfiguration.WriteTo.OpenTelemetry(options => {
            options.Endpoint = endpoint;
            if (!string.IsNullOrWhiteSpace(apiKey))
                options.Headers = new Dictionary<string, string> { ["api-key"] = apiKey };

            options.BatchingOptions.BatchSizeLimit = 1_000;
            options.BatchingOptions.BufferingTimeLimit = TimeSpan.FromSeconds(2);
            options.BatchingOptions.QueueLimit = 100_000;
        });
    }
}
