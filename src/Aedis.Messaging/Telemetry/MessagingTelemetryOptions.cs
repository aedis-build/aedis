namespace Aedis.Messaging.Telemetry;

/// <summary>
///     Configuração da instrumentação de mensageria (seção <c>Messaging:Telemetry</c>). Spans e métricas
///     saem por <c>System.Diagnostics</c> (ActivitySource/Meter <c>Aedis.Messaging</c>) e são exportados pelo
///     pipeline de observabilidade do host; tudo ligado por padrão.
/// </summary>
public sealed class MessagingTelemetryOptions
{
    /// <summary>Nome da seção de configuração (<c>Messaging:Telemetry</c>).</summary>
    public const string SectionName = "Messaging:Telemetry";

    /// <summary>Cria spans <c>publish {destino}</c> (Producer) e <c>process {destino}</c> (Consumer). Padrão: ligado.</summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>Emite contadores/histogramas de publish, consumo, duração, idade e em-voo. Padrão: ligado.</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>Injeta <c>traceparent</c>/<c>tracestate</c> na mensagem e restaura o contexto no consumidor. Padrão: ligado.</summary>
    public bool PropagateTraceContext { get; set; } = true;

    /// <summary>Registra o histograma <c>aedis.messaging.message.age</c> (agora − <c>Date</c> da mensagem) no consumo. Padrão: ligado.</summary>
    public bool RecordMessageAge { get; set; } = true;
}
