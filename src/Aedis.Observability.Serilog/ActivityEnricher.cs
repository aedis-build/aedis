using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Aedis.Observability.Serilog;

/// <summary>
///     Correlação log↔trace em formato decimal (seção <c>Logging:TraceCorrelation</c>). Os ids W3C em hex já
///     saem nativos do Serilog (<c>@tr</c>/<c>@sp</c> no JSON compacto, <c>trace_id</c>/<c>span_id</c> no OTLP);
///     alguns backends, porém, ligam logs a spans pelos 64 bits baixos em decimal. Opt-in: desligado por padrão.
/// </summary>
public sealed class TraceCorrelationOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "Logging:TraceCorrelation";

    /// <summary>Emite os ids decimais. Padrão: não.</summary>
    public bool DecimalIds { get; set; }

    /// <summary>Nome da propriedade com o TraceId decimal. Padrão <c>dd.trace_id</c>.</summary>
    public string TraceIdProperty { get; set; } = "dd.trace_id";

    /// <summary>Nome da propriedade com o SpanId decimal. Padrão <c>dd.span_id</c>.</summary>
    public string SpanIdProperty { get; set; } = "dd.span_id";

    /// <summary>Lê as opções da configuração (padrões quando a seção não existe).</summary>
    public static TraceCorrelationOptions FromConfiguration(IConfiguration configuration) =>
        configuration.GetSection(SectionName).Get<TraceCorrelationOptions>() ?? new TraceCorrelationOptions();
}

/// <summary>
///     Enriquecedor que, com um <see cref="Activity" /> W3C ativo, adiciona o TraceId e o SpanId em decimal
///     (64 bits baixos) sob os nomes configurados. Usa <c>AddPropertyIfAbsent</c> para não sobrescrever uma
///     injeção feita por um agente de APM.
/// </summary>
public sealed class ActivityEnricher : ILogEventEnricher
{
    private readonly TraceCorrelationOptions _options;

    /// <summary>Cria o enriquecedor com os nomes das propriedades.</summary>
    public ActivityEnricher(TraceCorrelationOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) {
        var activity = Activity.Current;
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C) return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(_options.TraceIdProperty, ToDecimalId(activity.TraceId.ToHexString())));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(_options.SpanIdProperty, ToDecimalId(activity.SpanId.ToHexString())));
    }

    /// <summary>Converte um id hexadecimal nos seus 64 bits baixos em decimal.</summary>
    public static string ToDecimalId(string hex) {
        var low64 = hex.Length > 16 ? hex[^16..] : hex;
        return ulong.TryParse(low64, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : hex;
    }
}
