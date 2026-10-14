using System.Diagnostics;
using System.Diagnostics.Metrics;
using Aedis.Observability.Abstractions;
using Aedis.Observability.Serilog;
using Aedis.Observability.Serilog.BusinessEvents;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Xunit;

namespace Aedis.Observability.Tests;

/// <summary>
///     Eventos de negócio: uma emissão vira log <c>LogType=Event</c> com propriedades <c>event.*</c>, contador sem
///     identificadores nas tags, evento no span e registro normalizado (com atributos mascarados) para os sinks.
/// </summary>
public sealed class BusinessEventLoggerTests
{
    private static (BusinessEventLogger Logger, List<LogEvent> Captured, IDisposable Dispose) Build(
        BusinessEventOptions? options = null, params IBusinessEventSink[] sinks) {
        var captured = new List<LogEvent>();
        var configuration = new ConfigurationBuilder().Build();
        var loggerConfiguration = new LoggerConfiguration();
        AedisSerilog.Configure(loggerConfiguration, configuration);
        loggerConfiguration.WriteTo.Sink(new CaptureSink(captured));
        var serilog = loggerConfiguration.CreateLogger();
        var factory = new SerilogLoggerFactory(serilog);

        var logger = new BusinessEventLogger(factory.CreateLogger<BusinessEventLogger>(),
            Options.Create(options ?? new BusinessEventOptions()), RedactionOptions.FromConfiguration(configuration), sinks);
        return (logger, captured, new Disposer(factory, serilog));
    }

    [Fact]
    public void Emit_gera_log_evento_com_propriedades_event() {
        var (logger, captured, dispose) = Build();
        using (dispose) {
            logger.Emit("orders.order.confirmed", subject: "ord-1", attributes: ("channel", "web"));
        }

        var logEvent = captured.Should().ContainSingle().Subject;
        logEvent.Level.Should().Be(LogEventLevel.Information);
        Text(logEvent, "LogType").Should().Be("Event");
        Text(logEvent, "event.name").Should().Be("orders.order.confirmed");
        Text(logEvent, "event.domain").Should().Be("orders");
        Text(logEvent, "event.action").Should().Be("confirmed");
        Text(logEvent, "event.outcome").Should().Be("success");
        Text(logEvent, "event.subject").Should().Be("ord-1");
        Text(logEvent, "event.author").Should().Be("system");
        Text(logEvent, "channel").Should().Be("web");
    }

    [Fact]
    public void Contador_nao_leva_identificadores_nas_tags() {
        var measurements = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => {
            if (instrument.Name == "aedis.business.events") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => measurements.Add(tags.ToArray()));
        listener.Start();

        var (logger, _, dispose) = Build();
        using (dispose) {
            logger.Emit("orders.order.cancelled", BusinessEventOutcome.Failure, "ord-9", "expired", ("partner_id", "p-1"));
        }

        var tags = measurements.Should().ContainSingle().Subject.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().Contain("event.domain", "orders");
        tags.Should().Contain("event.name", "orders.order.cancelled");
        tags.Should().Contain("event.outcome", "failure");
        tags.Keys.Should().NotContain(["event.subject", "partner_id", "event.reason"]);
    }

    [Fact]
    public void Metrica_desligada_nao_incrementa() {
        var count = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => {
            if (instrument.Name == "aedis.business.events") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => Interlocked.Increment(ref count));
        listener.Start();

        var (logger, _, dispose) = Build(new BusinessEventOptions { EmitMetric = false });
        using (dispose) {
            logger.Emit("orders.order.confirmed");
        }

        count.Should().Be(0);
    }

    [Theory]
    [InlineData("orders.confirmed")]
    [InlineData("Orders.Order.Confirmed")]
    [InlineData("orders.order.confirmed.again")]
    [InlineData("orders..confirmed")]
    public void Nome_fora_do_formato_lanca(string name) {
        var (logger, _, dispose) = Build();
        using (dispose) {
            var act = () => logger.Emit(name);
            act.Should().Throw<ArgumentException>().WithMessage("*dominio.entidade.acao*");
        }
    }

    [Fact]
    public void Autor_informado_prevalece_sobre_o_padrao() {
        var (logger, captured, dispose) = Build(new BusinessEventOptions { DefaultAuthor = "worker" });
        using (dispose) {
            logger.Emit(new BusinessEvent { Name = "orders.order.confirmed", Author = "user-7" });
            logger.Emit(new BusinessEvent { Name = "orders.order.confirmed" });
        }

        Text(captured[0], "event.author").Should().Be("user-7");
        Text(captured[1], "event.author").Should().Be("worker");
    }

    [Fact]
    public void Correlacao_vem_da_conversation_id_do_span_de_consumo() {
        using var source = new ActivitySource("tests");
        using var listener = new ActivityListener {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        var (logger, captured, dispose) = Build();
        using (dispose) {
            using var consume = source.StartActivity("process")!;
            consume.SetTag("messaging.message.conversation_id", "conv-42");
            using var child = source.StartActivity("handler")!;
            logger.Emit("orders.order.confirmed");

            Text(captured.Single(), "correlation_id").Should().Be("conv-42");
            child.Events.Should().ContainSingle(e => e.Name == "orders.order.confirmed");
        }
    }

    [Fact]
    public void Sem_conversation_id_a_correlacao_e_o_TraceId() {
        using var source = new ActivitySource("tests");
        using var listener = new ActivityListener {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        var (logger, captured, dispose) = Build();
        using (dispose) {
            using var activity = source.StartActivity("op")!;
            logger.Emit("orders.order.confirmed");

            Text(captured.Single(), "correlation_id").Should().Be(activity.TraceId.ToHexString());
        }
    }

    [Fact]
    public void Sink_recebe_registro_normalizado_com_atributos_mascarados() {
        var sink = new RecordingSink();
        var (logger, _, dispose) = Build(sinks: sink);
        using (dispose) {
            logger.Emit(new BusinessEvent {
                Name = "accounts.user.registered",
                Subject = "u-1",
                Auditable = true,
                Attributes = new Dictionary<string, object?> { ["password"] = "hunter2", ["attempt"] = 3, ["note"] = "Bearer abc.def" }
            });
        }

        var record = sink.Records.Should().ContainSingle().Subject;
        record.Name.Should().Be("accounts.user.registered");
        record.Domain.Should().Be("accounts");
        record.Action.Should().Be("registered");
        record.Author.Should().Be("system");
        record.Auditable.Should().BeTrue();
        record.Attributes["attempt"].Should().Be("3");
        record.Attributes["password"].Should().NotContain("hunter2");
        record.Attributes["note"].Should().NotContain("abc.def");
    }

    [Fact]
    public void Sink_que_lanca_nao_derruba_a_emissao() {
        var healthy = new RecordingSink();
        var (logger, captured, dispose) = Build(sinks: [new ThrowingSink(), healthy]);
        using (dispose) {
            var act = () => logger.Emit("orders.order.confirmed");
            act.Should().NotThrow();
        }

        healthy.Records.Should().ContainSingle();
        captured.Should().Contain(e => e.Level == LogEventLevel.Warning);
    }

    [Fact]
    public void AddAedisBusinessEvents_resolve_o_logger() {
        var provider = new ServiceCollection().AddLogging()
            .AddAedisBusinessEvents(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Observability:BusinessEvents:DefaultAuthor"] = "batch"
            }).Build())
            .BuildServiceProvider();

        provider.GetRequiredService<IBusinessEventLogger>().Should().BeOfType<BusinessEventLogger>();
        provider.GetRequiredService<IOptions<BusinessEventOptions>>().Value.DefaultAuthor.Should().Be("batch");
    }

    private static string Text(LogEvent logEvent, string key) {
        logEvent.Properties.Should().ContainKey(key);
        return logEvent.Properties[key] is ScalarValue { Value: var v } ? v?.ToString() ?? "" : logEvent.Properties[key].ToString();
    }

    private sealed class CaptureSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }

    private sealed class RecordingSink : IBusinessEventSink
    {
        public List<BusinessEventRecord> Records { get; } = [];

        public void Observe(BusinessEventRecord record) => Records.Add(record);
    }

    private sealed class ThrowingSink : IBusinessEventSink
    {
        public void Observe(BusinessEventRecord record) => throw new InvalidOperationException("boom");
    }

    private sealed class Disposer(params IDisposable[] items) : IDisposable
    {
        public void Dispose() {
            foreach (var item in items) item.Dispose();
        }
    }
}
