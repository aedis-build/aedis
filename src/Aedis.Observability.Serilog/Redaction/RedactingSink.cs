using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace Aedis.Observability.Serilog;

/// <summary>
///     Sink decorador que reescreve o texto do template da mensagem pelo <see cref="TextRedactor" /> antes de
///     entregar o evento aos sinks reais. É o único ponto capaz de alcançar segredos em mensagens interpoladas
///     (<c>$"token {token}"</c>), que não viram propriedade e por isso escapam ao enriquecimento. Descarta o
///     sink interno junto consigo, para que lotes pendentes (OTLP) sejam esvaziados no desligamento.
/// </summary>
internal sealed class RedactingSink : ILogEventSink, IDisposable
{
    private readonly ILogEventSink _inner;
    private readonly TextRedactor _textRedactor;
    private readonly MessageTemplateParser _parser = new();
    private readonly bool _enabled;

    internal RedactingSink(ILogEventSink inner, RedactionOptions options) {
        _inner = inner;
        _textRedactor = new TextRedactor(options);
        _enabled = options.Enabled && options.MaskSecretsInText;
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent) {
        if (!_enabled) {
            _inner.Emit(logEvent);
            return;
        }

        var text = logEvent.MessageTemplate.Text;
        var redacted = _textRedactor.Redact(text);
        if (ReferenceEquals(redacted, text)) {
            _inner.Emit(logEvent);
            return;
        }

        var rewritten = new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.Exception,
            _parser.Parse(redacted),
            logEvent.Properties.Select(property => new LogEventProperty(property.Key, property.Value)),
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default);

        _inner.Emit(rewritten);
    }

    /// <inheritdoc />
    public void Dispose() {
        (_inner as IDisposable)?.Dispose();
    }
}
