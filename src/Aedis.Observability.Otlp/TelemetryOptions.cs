namespace Aedis.Observability.Otlp;

/// <summary>
///     Configuração unificada de telemetria (métricas, traces e logs) via OpenTelemetry/OTLP. Lida da
///     seção <c>Telemetry</c>. O OTLP é o protocolo universal — o mesmo endpoint serve Grafana/Prometheus
///     (via collector), DataDog (OTLP do agent), AppDynamics e Azure Monitor. Sem endpoint, nenhum
///     exporter é registrado (telemetria local apenas).
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>Nome da seção de configuração (<c>Telemetry</c>) de onde estas opções são lidas.</summary>
    public const string SectionName = "Telemetry";

    /// <summary>Endpoint OTLP (ex.: <c>http://otel-collector:4317</c>). Vazio = nenhum exporter OTLP.</summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>Chave de API opcional, enviada como header <c>api-key</c> ao collector.</summary>
    public string? OtlpApiKey { get; set; }

    /// <summary>Coleta/exporta métricas. Padrão true.</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>Coleta/exporta traces (distributed tracing). Padrão true.</summary>
    public bool EnableTraces { get; set; } = true;

    /// <summary>Exporta logs via OTLP (lido pelo provider Serilog). Padrão true.</summary>
    public bool EnableLogs { get; set; } = true;

    /// <summary>
    ///     Temporalidade do exporter OTLP de métricas: <c>"Delta"</c> ou <c>"Cumulative"</c>. Padrão Delta —
    ///     ingerido de forma limpa pelo DataDog; backends Prometheus-like preferem Cumulative.
    /// </summary>
    public string OtlpMetricsTemporality { get; set; } = "Delta";
}
