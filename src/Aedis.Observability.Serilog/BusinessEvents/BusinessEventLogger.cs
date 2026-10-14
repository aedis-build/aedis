using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.RegularExpressions;
using Aedis.Core.Utils;
using Aedis.Observability.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Observability.Serilog.BusinessEvents;

/// <summary>
///     Implementação de <see cref="IBusinessEventLogger" />: uma emissão, quatro saídas. (1) Log com
///     <c>LogType=Event</c> e propriedades <c>event.*</c> (os nomes com ponto chegam intactos ao OTLP);
///     (2) <c>ActivityEvent</c> no span corrente; (3) contador <c>aedis.business.events</c> só com tags de baixa
///     cardinalidade; (4) os <see cref="IBusinessEventSink" /> registrados recebem o registro normalizado, com
///     os atributos mascarados pela mesma política de redaction do log. Identificadores nunca viram tag de
///     métrica.
/// </summary>
public sealed partial class BusinessEventLogger : IBusinessEventLogger
{
    /// <summary>Nome do <see cref="Meter" /> dos eventos de negócio.</summary>
    public const string MeterName = "Aedis.Observability.BusinessEvents";

    private const string ConversationIdTag = "messaging.message.conversation_id";

    private static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> Events = Meter.CreateCounter<long>(
        "aedis.business.events", "{event}", "Eventos de negócio por domínio, nome e outcome");

    private readonly ILogger<BusinessEventLogger> _logger;
    private readonly BusinessEventOptions _options;
    private readonly Redactor _redactor;
    private readonly IReadOnlyList<IBusinessEventSink> _sinks;
    private readonly TextRedactor _textRedactor;

    /// <summary>Cria o logger com o destino de log, as opções, a política de redaction e os sinks.</summary>
    public BusinessEventLogger(ILogger<BusinessEventLogger> logger, IOptions<BusinessEventOptions> options,
        RedactionOptions redaction, IEnumerable<IBusinessEventSink> sinks) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(redaction);
        _redactor = new Redactor(redaction);
        _textRedactor = new TextRedactor(redaction);
        _sinks = (sinks ?? []).ToList();
    }

    /// <inheritdoc />
    public void Emit(BusinessEvent evt) {
        ArgumentNullException.ThrowIfNull(evt);
        if (!NamePattern().IsMatch(evt.Name))
            throw new ArgumentException(
                $"Nome de evento de negócio inválido: '{evt.Name}'. Use 'dominio.entidade.acao' (três segmentos, snake_case).", nameof(evt));

        var outcome = evt.Outcome.ToString().ToLowerInvariant();
        var author = string.IsNullOrWhiteSpace(evt.Author) ? _options.DefaultAuthor : evt.Author;
        var correlationId = evt.CorrelationId ?? ResolveCorrelationId();

        EmitLog(evt, outcome, author, correlationId);

        if (_options.EmitActivityEvent) EmitActivityEvent(evt, outcome, author);

        if (_options.EmitMetric)
            Events.Add(1, new TagList {
                { "application", ApplicationInfo.Name },
                { "event.domain", evt.Domain },
                { "event.name", evt.Name },
                { "event.outcome", outcome }
            });

        if (_sinks.Count > 0) Observe(ToRecord(evt, outcome, author, correlationId));
    }

    /// <inheritdoc />
    public void Emit(string name, BusinessEventOutcome outcome = BusinessEventOutcome.Success, string? subject = null,
        string? reason = null, params (string Key, object? Value)[] attributes) {
        Emit(new BusinessEvent {
            Name = name,
            Outcome = outcome,
            Subject = subject,
            Reason = reason,
            Attributes = attributes.Length == 0 ? null : attributes.ToDictionary(a => a.Key, a => a.Value)
        });
    }

    /// <summary>Correlação padrão: a <c>conversation_id</c> do span de consumo (subindo pelos pais) ou o TraceId ativo.</summary>
    public static string? ResolveCorrelationId() {
        var current = Activity.Current;
        if (current is null) return null;

        for (var activity = current; activity is not null; activity = activity.Parent)
            if (activity.GetTagItem(ConversationIdTag) is string conversationId && conversationId.Length > 0)
                return conversationId;

        return current.IdFormat == ActivityIdFormat.W3C ? current.TraceId.ToHexString() : null;
    }

    private void EmitLog(BusinessEvent evt, string outcome, string author, string? correlationId) {
        var scope = new Dictionary<string, object?> {
            ["LogType"] = "Event",
            ["event.name"] = evt.Name,
            ["event.domain"] = evt.Domain,
            ["event.action"] = evt.Action,
            ["event.outcome"] = outcome,
            ["event.kind"] = "event",
            ["event.author"] = author,
            ["event.auditable"] = evt.Auditable
        };
        if (evt.Reason is not null) scope["event.reason"] = evt.Reason;
        if (evt.Subject is not null) scope["event.subject"] = evt.Subject;
        if (correlationId is not null) scope["correlation_id"] = correlationId;
        if (evt.Attributes is not null)
            foreach (var (key, value) in evt.Attributes)
                scope[key] = value;

        using (_logger.BeginScope(scope)) {
            _logger.Log(_options.LogLevel, "{EventName} {EventOutcome}", evt.Name, outcome);
        }
    }

    private static void EmitActivityEvent(BusinessEvent evt, string outcome, string author) {
        var activity = Activity.Current;
        if (activity is null) return;

        var tags = new ActivityTagsCollection { { "event.outcome", outcome }, { "event.author", author } };
        if (evt.Subject is not null) tags["event.subject"] = evt.Subject;
        if (evt.Reason is not null) tags["event.reason"] = evt.Reason;
        if (evt.Attributes is not null)
            foreach (var (key, value) in evt.Attributes)
                tags[key] = value;

        activity.AddEvent(new ActivityEvent(evt.Name, tags: tags));
        activity.SetTag("aedis.event.last", evt.Name);
    }

    private BusinessEventRecord ToRecord(BusinessEvent evt, string outcome, string author, string? correlationId) {
        var activity = Activity.Current;
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (evt.Attributes is not null)
            foreach (var (key, value) in evt.Attributes)
                attributes[key] = RedactAttribute(key, value);

        return new BusinessEventRecord(evt.Name, evt.Domain, evt.Action, outcome, evt.Reason, evt.Subject, author, evt.Auditable,
            correlationId, ApplicationInfo.Name, ApplicationInfo.UtcNow,
            activity?.IdFormat == ActivityIdFormat.W3C ? activity.TraceId.ToHexString() : null,
            activity?.IdFormat == ActivityIdFormat.W3C ? activity.SpanId.ToHexString() : null,
            attributes);
    }

    private string? RedactAttribute(string key, object? value) {
        var text = value switch {
            null => null,
            string s => s,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        if (text is null) return null;

        var classification = _redactor.Classify(key);
        return classification == Redactor.Classification.None
            ? _textRedactor.Redact(text)
            : _redactor.Apply(text, _redactor.StrategyFor(classification));
    }

    private void Observe(BusinessEventRecord record) {
        foreach (var sink in _sinks)
            try {
                sink.Observe(record);
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "O sink de eventos de negócio {Sink} falhou ao observar {EventName}.", sink.GetType().Name, record.Name);
            }
    }

    [GeneratedRegex("^[a-z0-9_]+\\.[a-z0-9_]+\\.[a-z0-9_]+$")]
    private static partial Regex NamePattern();
}
