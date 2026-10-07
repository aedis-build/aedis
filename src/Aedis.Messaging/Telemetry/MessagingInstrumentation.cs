using System.Diagnostics;
using System.Diagnostics.Metrics;
using Aedis.Messaging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aedis.Messaging.Telemetry;

/// <summary>
///     Ponto único de instrumentação de mensageria para todos os providers: spans <c>publish {destino}</c> e
///     <c>process {destino}</c> seguindo as convenções semânticas de messaging do OpenTelemetry, propagação
///     W3C (<c>traceparent</c>/<c>tracestate</c>) pelo <see cref="DistributedContextPropagator" /> do runtime e
///     métricas de baixa cardinalidade. Identificadores (correlação, id de mensagem) entram só como atributos
///     de span — nunca como tag de métrica. Os providers chamam <see cref="StartPublish" /> e
///     <see cref="StartProcess" /> e fecham a operação com <see cref="MessagingOperation.Complete" />.
/// </summary>
public static class MessagingInstrumentation
{
    /// <summary>Nome do <see cref="ActivitySource" /> e do <see cref="Meter" /> (<c>Aedis.Messaging</c>).</summary>
    public const string SourceName = "Aedis.Messaging";

    /// <summary>Chave do carrier que transporta o <c>traceparent</c> W3C.</summary>
    public const string TraceParentHeader = "traceparent";

    /// <summary>Chave do carrier que transporta o <c>tracestate</c> W3C.</summary>
    public const string TraceStateHeader = "tracestate";

    private static readonly ActivitySource Source = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    internal static readonly Counter<long> SentMessages = Meter.CreateCounter<long>(
        "messaging.client.sent.messages", "{message}", "Mensagens publicadas");

    internal static readonly Counter<long> ConsumedMessages = Meter.CreateCounter<long>(
        "messaging.client.consumed.messages", "{message}", "Mensagens consumidas, por desfecho");

    internal static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>(
        "messaging.publish.duration", "s", "Duração do publish");

    internal static readonly Histogram<double> ProcessDuration = Meter.CreateHistogram<double>(
        "messaging.process.duration", "s", "Duração do processamento (handler), por desfecho");

    internal static readonly Histogram<double> MessageAge = Meter.CreateHistogram<double>(
        "aedis.messaging.message.age", "s", "Idade da mensagem no consumo (agora - Date)");

    internal static readonly UpDownCounter<long> InFlight = Meter.CreateUpDownCounter<long>(
        "aedis.messaging.inflight", "{message}", "Mensagens em processamento");

    /// <summary>Opções ativas. Definidas por <see cref="Configure" /> (ou <c>AddAedisMessagingTelemetry</c>); padrão: tudo ligado.</summary>
    public static MessagingTelemetryOptions Options { get; private set; } = new();

    /// <summary>Substitui as opções ativas da instrumentação.</summary>
    public static void Configure(MessagingTelemetryOptions options) => Options = options ?? new MessagingTelemetryOptions();

    /// <summary>
    ///     Inicia a operação de publicação de <paramref name="message" /> em <paramref name="destination" />.
    ///     Feche com <see cref="MessagingOperation.Complete" /> ou descarte (sucesso implícito).
    /// </summary>
    /// <param name="system">Identificador do broker (ex.: <c>ibm_mq</c>, <c>aws_sqs</c>, <c>rabbitmq</c>).</param>
    /// <param name="destination">Fila, tópico ou exchange de destino.</param>
    /// <param name="message">Mensagem publicada; fornece correlação, tipo e nome do evento ao span.</param>
    /// <param name="logger">Logger opcional que recebe um escopo com <c>CorrelationId</c> enquanto a operação durar.</param>
    public static MessagingOperation StartPublish(string system, string destination, IMessage message, ILogger? logger = null) {
        var activity = Options.EnableTracing
            ? Source.StartActivity($"publish {destination}", ActivityKind.Producer)
            : null;

        var operation = new MessagingOperation(activity, system, destination, "publish", message.GetType().Name, logger);
        operation.ApplyCommonTags();
        operation.ApplyMessageTags(message);
        operation.PushCorrelationId(message.CorrelationId);
        return operation;
    }

    /// <summary>
    ///     Inicia a operação de consumo antes da desserialização. O pai do span vem do carrier
    ///     (<c>traceparent</c>) ou, na ausência dele, do identificador de correlação de transporte quando ele é
    ///     um TraceId W3C (mensagens publicadas dentro de uma requisição usam o TraceId como correlação).
    ///     Chame <see cref="MessagingOperation.SetMessage" /> após desserializar.
    /// </summary>
    /// <param name="system">Identificador do broker.</param>
    /// <param name="destination">Fila de onde a mensagem foi lida.</param>
    /// <param name="carrierGetter">Leitura de atributos/cabeçalhos da mensagem por nome; <c>null</c> quando o transporte não tem cabeçalhos.</param>
    /// <param name="transportCorrelationId">Correlação lida do transporte (ex.: <c>MQMD.CorrelId</c>).</param>
    /// <param name="messageId">Identificador nativo da mensagem no broker.</param>
    /// <param name="receiveCount">Número da tentativa de entrega, quando o broker informa.</param>
    /// <param name="logger">Logger opcional que recebe um escopo com <c>CorrelationId</c> enquanto a operação durar.</param>
    public static MessagingOperation StartProcess(string system, string destination,
        Func<string, string?>? carrierGetter, string? transportCorrelationId = null, string? messageId = null,
        int? receiveCount = null, ILogger? logger = null) {
        Activity? activity = null;

        if (Options.EnableTracing) {
            var parent = ExtractParent(carrierGetter, transportCorrelationId, out var parentSource);
            activity = Source.StartActivity($"process {destination}", ActivityKind.Consumer, parent);
            activity?.SetTag("aedis.messaging.parent_source", parentSource);
        }

        var operation = new MessagingOperation(activity, system, destination, "process", null, logger);
        operation.ApplyCommonTags();

        if (messageId is not null) operation.SetTag("messaging.message.id", messageId);
        if (receiveCount is not null) operation.SetTag("aedis.messaging.receive_count", receiveCount);
        if (!string.IsNullOrEmpty(transportCorrelationId)) {
            operation.SetTag("messaging.message.conversation_id", transportCorrelationId);
            operation.PushCorrelationId(transportCorrelationId);
        }

        if (Options.EnableMetrics)
            InFlight.Add(1, operation.MetricTags());

        return operation;
    }

    /// <summary>
    ///     Injeta <c>traceparent</c>/<c>tracestate</c> do span <paramref name="activity" /> (ou do
    ///     <see cref="Activity.Current" />) no carrier da mensagem. Não faz nada com a propagação desligada.
    /// </summary>
    public static void InjectContext(Activity? activity, Action<string, string> carrierSetter) {
        if (!Options.PropagateTraceContext) return;

        var source = activity ?? Activity.Current;
        if (source is null) return;

        DistributedContextPropagator.Current.Inject(source, carrierSetter,
            static (carrier, key, value) => ((Action<string, string>)carrier!)(key, value));
    }

    private static ActivityContext ExtractParent(Func<string, string?>? carrierGetter, string? transportCorrelationId,
        out string parentSource) {
        if (Options.PropagateTraceContext && carrierGetter is not null) {
            DistributedContextPropagator.Current.ExtractTraceIdAndState(carrierGetter,
                static (object? carrier, string key, out string? value, out IEnumerable<string>? values) => {
                    value = ((Func<string, string?>)carrier!)(key);
                    values = null;
                }, out var traceParent, out var traceState);

            if (traceParent is not null && ActivityContext.TryParse(traceParent, traceState, true, out var context)) {
                parentSource = "traceparent";
                return context;
            }
        }

        if (TryParseTraceId(transportCorrelationId, out var traceId)) {
            parentSource = "correlation_id";
            return new ActivityContext(traceId, default, ActivityTraceFlags.Recorded, isRemote: true);
        }

        parentSource = "none";
        return default;
    }

    /// <summary>Reconhece um TraceId W3C (32 hexadecimais minúsculos, não todo zero) em <paramref name="value" />.</summary>
    public static bool TryParseTraceId(string? value, out ActivityTraceId traceId) {
        traceId = default;
        if (value is not { Length: 32 }) return false;

        var allZero = true;
        foreach (var c in value) {
            if (!char.IsAsciiHexDigitLower(c)) return false;
            if (c != '0') allZero = false;
        }

        if (allZero) return false;

        traceId = ActivityTraceId.CreateFromString(value);
        return true;
    }
}
