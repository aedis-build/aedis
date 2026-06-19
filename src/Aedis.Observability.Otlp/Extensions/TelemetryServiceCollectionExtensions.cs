using System.Diagnostics.Metrics;
using Aedis.Core.Utils;
using Aedis.Observability.Abstractions;
using Aedis.Observability.Otlp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Instrumentação de telemetria do Aedis via OpenTelemetry: métricas e traces exportados por OTLP — o
///     protocolo universal que serve Grafana/Prometheus (via collector), DataDog, AppDynamics e Azure
///     Monitor. <c>AddMeter("*")</c> coleta qualquer métrica customizada (ver <see cref="BaseMetrics{T}" />)
///     por default. Logs OTLP são configurados no provider Serilog (mesma seção <c>Telemetry</c>).
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    ///     Registra métricas e traces (OTel) com exporter OTLP, instrumentação automática (ASP.NET Core,
    ///     HttpClient, runtime) e health check do OTLP. Os callbacks permitem adicionar exporters extras
    ///     (ex.: Prometheus direto) sem o Aedis depender desses pacotes.
    /// </summary>
    public static IServiceCollection AddAedisTelemetry(this IServiceCollection services,
        IConfiguration configuration, Action<MeterProviderBuilder>? configureMetrics = null,
        Action<TracerProviderBuilder>? configureTracing = null) {
        services.AddMetrics();
        services.Configure<TelemetryOptions>(configuration.GetSection(TelemetryOptions.SectionName));

        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
                      ?? new TelemetryOptions();
        var endpoint = options.OtlpEndpoint;
        var apiKey = options.OtlpApiKey;

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ApplicationInfo.Name));

        if (options.EnableMetrics)
            otel.WithMetrics(builder => {
                builder.AddMeter("*")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(endpoint))
                    builder.AddOtlpExporter((exporter, reader) => {
                        ConfigureOtlp(exporter, endpoint, apiKey);
                        reader.TemporalityPreference = ParseTemporality(options.OtlpMetricsTemporality);
                    });

                configureMetrics?.Invoke(builder);
            });

        if (options.EnableTraces)
            otel.WithTracing(builder => {
                builder.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(endpoint))
                    builder.AddOtlpExporter(exporter => ConfigureOtlp(exporter, endpoint, apiKey));

                configureTracing?.Invoke(builder);
            });

        services.AddHttpClient("otlp-health", client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddHealthChecks().AddCheck<OtlpExporterHealthCheck>("otlp", tags: ["ready"]);

        return services;
    }

    /// <summary>
    ///     Registra uma classe de métricas customizadas derivada de <see cref="BaseMetrics{T}" /> (singleton,
    ///     com o <see cref="IMeterFactory" /> injetado). O Meter criado é coletado por default pela telemetria.
    /// </summary>
    public static IServiceCollection AddCustomMetrics<TMetric>(this IServiceCollection services)
        where TMetric : BaseMetrics<TMetric> {
        services.TryAddSingleton(typeof(TMetric), sp =>
            Activator.CreateInstance(typeof(TMetric), sp.GetRequiredService<IMeterFactory>())
            ?? throw new InvalidOperationException(
                $"Falha ao criar '{typeof(TMetric).FullName}'. Garanta um construtor público com IMeterFactory."));
        return services;
    }

    private static void ConfigureOtlp(OtlpExporterOptions exporter, string endpoint, string? apiKey) {
        exporter.Endpoint = new Uri(endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
            exporter.Headers = $"api-key={apiKey}";
    }

    private static MetricReaderTemporalityPreference ParseTemporality(string? value) =>
        string.Equals(value, "Cumulative", StringComparison.OrdinalIgnoreCase)
            ? MetricReaderTemporalityPreference.Cumulative
            : MetricReaderTemporalityPreference.Delta;
}
