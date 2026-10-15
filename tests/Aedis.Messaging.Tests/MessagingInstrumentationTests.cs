using System.Diagnostics;
using System.Diagnostics.Metrics;
using Aedis.Exceptions;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Telemetry;
using FluentAssertions;
using Xunit;

namespace Aedis.Messaging.Tests;

/// <summary>
///     Instrumentação única de mensageria: spans publish/process com as convenções semânticas, pai restaurado
///     do <c>traceparent</c> ou do CorrelationId W3C, desfecho e duração nas métricas — e identificadores
///     nunca como tag de métrica.
/// </summary>
[Collection("MessagingInstrumentation")]
public sealed class MessagingInstrumentationTests : IDisposable
{
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _listener;

    public MessagingInstrumentationTests() {
        _listener = new ActivityListener {
            ShouldListenTo = source => source.Name == MessagingInstrumentation.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
        MessagingInstrumentation.Configure(new MessagingTelemetryOptions());
    }

    public void Dispose() => _listener.Dispose();

    private sealed class PingMessage : MessageBase
    {
        public override string EventName => "ping";
    }

    [Fact]
    public void Publish_cria_span_producer_com_as_tags_semconv() {
        var message = new PingMessage { CorrelationId = "corr-1" };

        using (MessagingInstrumentation.StartPublish("ibm_mq", "FILA", message)) {
        }

        var span = _activities.Should().ContainSingle().Subject;
        span.Kind.Should().Be(ActivityKind.Producer);
        span.DisplayName.Should().Be("publish FILA");
        span.GetTagItem("messaging.system").Should().Be("ibm_mq");
        span.GetTagItem("messaging.destination.name").Should().Be("FILA");
        span.GetTagItem("messaging.operation.type").Should().Be("publish");
        span.GetTagItem("messaging.message.conversation_id").Should().Be("corr-1");
        span.GetTagItem("aedis.message.event_name").Should().Be("ping");
        span.GetTagItem("aedis.messaging.outcome").Should().Be("success");
    }

    [Fact]
    public void Process_restaura_o_pai_do_traceparent() {
        var carrier = new Dictionary<string, string> {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
        };

        using (var operation = MessagingInstrumentation.StartProcess("aws_sqs", "fila", key => carrier.GetValueOrDefault(key))) {
            operation.Complete(MessageOutcome.Success);
        }

        var span = _activities.Should().ContainSingle().Subject;
        span.Kind.Should().Be(ActivityKind.Consumer);
        span.TraceId.ToString().Should().Be("0af7651916cd43dd8448eb211c80319c");
        span.ParentSpanId.ToString().Should().Be("b7ad6b7169203331");
        span.GetTagItem("aedis.messaging.parent_source").Should().Be("traceparent");
    }

    [Fact]
    public void Process_usa_o_correlation_id_como_trace_id_quando_e_w3c() {
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        using (MessagingInstrumentation.StartProcess("ibm_mq", "fila", null, traceId, "MSG1", 2)) {
        }

        var span = _activities.Should().ContainSingle().Subject;
        span.TraceId.ToString().Should().Be(traceId);
        span.GetTagItem("aedis.messaging.parent_source").Should().Be("correlation_id");
        span.GetTagItem("messaging.message.id").Should().Be("MSG1");
        span.GetTagItem("aedis.messaging.receive_count").Should().Be(2);
    }

    [Fact]
    public void Correlation_id_que_nao_e_trace_id_vira_raiz() {
        using (MessagingInstrumentation.StartProcess("ibm_mq", "fila", null, "pedido-42")) {
        }

        _activities.Should().ContainSingle().Which.GetTagItem("aedis.messaging.parent_source").Should().Be("none");
    }

    [Fact]
    public void Falha_permanente_marca_o_span_com_erro_e_tipo_da_excecao() {
        using (var operation = MessagingInstrumentation.StartProcess("ibm_mq", "fila", null)) {
            operation.Complete(MessageOutcome.PermanentFailure, new InvalidOperationException("boom"));
        }

        var span = _activities.Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.GetTagItem("error.type").Should().Be(nameof(InvalidOperationException));
        span.GetTagItem("aedis.messaging.outcome").Should().Be("permanent_failure");
    }

    [Fact]
    public void Consumo_conta_por_desfecho_sem_identificadores_nas_tags() {
        var measurements = new List<(long Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener {
            InstrumentPublished = (instrument, listener) => {
                if (instrument.Meter.Name == MessagingInstrumentation.SourceName && instrument.Name == "messaging.client.consumed.messages")
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Add((value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        meterListener.Start();

        using (var operation = MessagingInstrumentation.StartProcess("ibm_mq", "fila", null, "corr-xyz", "MSG9")) {
            operation.SetMessage(new PingMessage());
            operation.Complete(MessageOutcome.Retryable, new ServiceTemporarilyUnavailableException("db", "fora"));
        }

        var (value, tags) = measurements.Should().ContainSingle().Subject;
        value.Should().Be(1);
        tags["aedis.messaging.outcome"].Should().Be("retryable");
        tags["messaging.destination.name"].Should().Be("fila");
        tags["aedis.message.type"].Should().Be(nameof(PingMessage));
        tags.Keys.Should().NotContain(["messaging.message.id", "messaging.message.conversation_id"]);
    }

    [Fact]
    public void Inject_escreve_traceparent_no_carrier_e_respeita_a_opcao() {
        var carrier = new Dictionary<string, string>();

        using (var activity = new ActivitySource("Aedis.Tests").StartActivity("x") ?? new Activity("x").Start()) {
            MessagingInstrumentation.InjectContext(activity, (key, value) => carrier[key] = value);
        }

        carrier.Should().ContainKey("traceparent");

        MessagingInstrumentation.Configure(new MessagingTelemetryOptions { PropagateTraceContext = false });
        var untouched = new Dictionary<string, string>();
        using (var activity = new Activity("y").Start()) {
            MessagingInstrumentation.InjectContext(activity, (key, value) => untouched[key] = value);
        }

        untouched.Should().BeEmpty();
    }

    [Theory]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736", true)]
    [InlineData("00000000000000000000000000000000", false)]
    [InlineData("4BF92F3577B34DA6A3CE929D0E0E4736", false)]
    [InlineData("curto", false)]
    [InlineData(null, false)]
    public void TryParseTraceId_reconhece_so_trace_id_w3c(string? value, bool esperado) {
        MessagingInstrumentation.TryParseTraceId(value, out _).Should().Be(esperado);
    }
}
