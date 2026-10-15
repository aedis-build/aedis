using System.Diagnostics;
using Aedis.Observability.Serilog;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Aedis.Observability.Tests;

/// <summary>
///     Correlação log↔trace em decimal é opt-in (<c>Logging:TraceCorrelation:DecimalIds</c>): desligada, nenhuma
///     propriedade extra; ligada, TraceId/SpanId em decimal (64 bits baixos) sob os nomes configurados, sem
///     sobrescrever o que já existe.
/// </summary>
public sealed class ActivityEnricherTests
{
    [Theory]
    [InlineData("0000000000000000000000000000002a", "42")]
    [InlineData("ffffffffffffffff0000000000000010", "16")]
    [InlineData("000000000000002a", "42")]
    public void Converte_os_64_bits_baixos_em_decimal(string hex, string expected) =>
        ActivityEnricher.ToDecimalId(hex).Should().Be(expected);

    [Fact]
    public void Desligado_por_padrao_nao_adiciona_propriedades() {
        var captured = Capture(new ConfigurationBuilder().Build(), withActivity: true);

        captured.Properties.Keys.Should().NotContain(["dd.trace_id", "dd.span_id"]);
    }

    [Fact]
    public void Ligado_adiciona_ids_decimais_com_span_ativo() {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Logging:TraceCorrelation:DecimalIds"] = "true"
        }).Build();

        var captured = Capture(configuration, withActivity: true);

        captured.Properties.Should().ContainKey("dd.trace_id");
        captured.Properties.Should().ContainKey("dd.span_id");
        ulong.TryParse(((ScalarValue)captured.Properties["dd.trace_id"]).Value!.ToString(), out _).Should().BeTrue();
    }

    [Fact]
    public void Nomes_das_propriedades_sao_configuraveis() {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Logging:TraceCorrelation:DecimalIds"] = "true",
            ["Logging:TraceCorrelation:TraceIdProperty"] = "trace_dec",
            ["Logging:TraceCorrelation:SpanIdProperty"] = "span_dec"
        }).Build();

        var captured = Capture(configuration, withActivity: true);

        captured.Properties.Keys.Should().Contain(["trace_dec", "span_dec"]);
    }

    [Fact]
    public void Sem_span_ativo_nao_adiciona_nada() {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Logging:TraceCorrelation:DecimalIds"] = "true"
        }).Build();

        var captured = Capture(configuration, withActivity: false);

        captured.Properties.Keys.Should().NotContain(["dd.trace_id", "dd.span_id"]);
    }

    private static LogEvent Capture(IConfiguration configuration, bool withActivity) {
        var captured = new List<LogEvent>();
        var loggerConfiguration = new LoggerConfiguration();
        AedisSerilog.Configure(loggerConfiguration, configuration);
        loggerConfiguration.WriteTo.Sink(new CaptureSink(captured));

        using var listener = new ActivityListener {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("tests");

        using (var logger = loggerConfiguration.CreateLogger()) {
            using var activity = withActivity ? source.StartActivity("op") : null;
            logger.Information("ping");
        }

        return captured.Should().ContainSingle().Subject;
    }

    private sealed class CaptureSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
