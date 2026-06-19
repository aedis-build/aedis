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

    internal static void Configure(LoggerConfiguration loggerConfiguration, IConfiguration configuration) {
        loggerConfiguration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.HealthChecks", LogEventLevel.Warning);

        ApplyConfiguredLevels(loggerConfiguration, configuration);

        loggerConfiguration
            .Filter.ByExcluding(IsNoise)
            .Enrich.With(new LogTypeEnricher("Application"))
            .Enrich.FromLogContext()
            .WriteTo.Console(new CompactJsonFormatter()); // stdout — sempre (entrega durável)

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

    /// <summary>Sink OTLP de logs — opt-in via <c>Telemetry:OtlpEndpoint</c> (em lote, para entrega ao backend).</summary>
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

            // Lote + fila grande: agrega para throughput e absorve picos sem bloquear nem perder eventos.
            options.BatchingOptions.BatchSizeLimit = 1_000;
            options.BatchingOptions.BufferingTimeLimit = TimeSpan.FromSeconds(2);
            options.BatchingOptions.QueueLimit = 100_000;
        });
    }
}
