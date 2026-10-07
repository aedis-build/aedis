using System.Diagnostics;
using Aedis.Core.Utils;
using Aedis.Messaging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aedis.Messaging.Telemetry;

/// <summary>
///     Uma operação de publish ou process em andamento, criada por <see cref="MessagingInstrumentation" />.
///     Fecha o span, registra duração e desfecho nas métricas e mantém, enquanto viver, um escopo de log com
///     o <c>CorrelationId</c> da mensagem. Descartar sem <see cref="Complete" /> conta como sucesso.
/// </summary>
public sealed class MessagingOperation : IDisposable
{
    private readonly ILogger? _logger;
    private readonly string _operation;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private bool _completed;
    private IDisposable? _logScope;
    private string? _messageType;

    internal MessagingOperation(Activity? activity, string system, string destination, string operation,
        string? messageType, ILogger? logger) {
        Activity = activity;
        System = system;
        Destination = destination;
        _operation = operation;
        _messageType = messageType;
        _logger = logger;
    }

    /// <summary>Span da operação; <c>null</c> quando o tracing está desligado ou ninguém escuta a fonte.</summary>
    public Activity? Activity { get; }

    /// <summary>Identificador do broker.</summary>
    public string System { get; }

    /// <summary>Fila, tópico ou exchange da operação.</summary>
    public string Destination { get; }

    /// <summary>Fecha a operação como sucesso caso ainda não tenha sido fechada, e libera o span e o escopo de log.</summary>
    public void Dispose() {
        Complete(MessageOutcome.Success);
        _logScope?.Dispose();
        Activity?.Dispose();
    }

    /// <summary>
    ///     Após a desserialização no consumo: anexa identificadores da mensagem ao span, liga o escopo de log
    ///     ao <c>CorrelationId</c> dela e registra a idade da mensagem.
    /// </summary>
    public void SetMessage(IMessage message) {
        _messageType = message.GetType().Name;
        ApplyMessageTags(message);
        PushCorrelationId(message.CorrelationId);

        if (MessagingInstrumentation.Options is { EnableMetrics: true, RecordMessageAge: true } && message.Date != default) {
            var age = (ApplicationInfo.UtcNow - message.Date).TotalSeconds;
            if (age >= 0) MessagingInstrumentation.MessageAge.Record(age, MetricTags());
        }
    }

    /// <summary>Adiciona um atributo ao span (ignorado quando não há span).</summary>
    public void SetTag(string key, object? value) => Activity?.SetTag(key, value);

    /// <summary>
    ///     Fecha a operação com o <paramref name="outcome" /> informado: status do span, duração e contadores
    ///     por desfecho. Idempotente — a primeira chamada vence.
    /// </summary>
    public void Complete(MessageOutcome outcome, Exception? exception = null) {
        if (_completed) return;
        _completed = true;

        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;
        var outcomeTag = MessageOutcomeClassifier.ToTagValue(outcome);
        var errorType = exception?.GetType().Name;

        if (Activity is not null) {
            Activity.SetTag("aedis.messaging.outcome", outcomeTag);
            if (errorType is not null) Activity.SetTag("error.type", errorType);

            if (outcome is MessageOutcome.PermanentFailure or MessageOutcome.ExternalPermanent or MessageOutcome.UnhandledFailure)
                Activity.SetStatus(ActivityStatusCode.Error, exception?.Message);
            else
                Activity.SetStatus(ActivityStatusCode.Ok);
        }

        if (!MessagingInstrumentation.Options.EnableMetrics) return;

        var tags = MetricTags();
        if (_operation == "publish") {
            MessagingInstrumentation.SentMessages.Add(1, tags);
            MessagingInstrumentation.PublishDuration.Record(elapsed, tags);
            return;
        }

        MessagingInstrumentation.InFlight.Add(-1, tags);

        var outcomeTags = new TagList(tags.AsSpan()) { { "aedis.messaging.outcome", outcomeTag } };
        if (errorType is not null) outcomeTags.Add("error.type", errorType);

        MessagingInstrumentation.ConsumedMessages.Add(1, outcomeTags);
        MessagingInstrumentation.ProcessDuration.Record(elapsed, outcomeTags);
    }

    internal void PushCorrelationId(string? correlationId) {
        if (_logScope is not null || _logger is null || string.IsNullOrEmpty(correlationId)) return;
        _logScope = _logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
    }

    internal void ApplyCommonTags() {
        if (Activity is null) return;
        Activity.SetTag("messaging.system", System);
        Activity.SetTag("messaging.destination.name", Destination);
        Activity.SetTag("messaging.operation.type", _operation);
    }

    internal void ApplyMessageTags(IMessage message) {
        if (Activity is null) return;
        Activity.SetTag("messaging.message.conversation_id", message.CorrelationId);
        Activity.SetTag("aedis.message.type", message.GetType().Name);
        Activity.SetTag("aedis.message.event_name", message.EventName);
    }

    internal KeyValuePair<string, object?>[] MetricTags() => [
        new("application", ApplicationInfo.Name),
        new("messaging.system", System),
        new("messaging.destination.name", Destination),
        new("aedis.message.type", _messageType ?? "unknown")
    ];
}
