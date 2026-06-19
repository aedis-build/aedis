using System.Diagnostics.Metrics;
using Aedis.Observability.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Observability.Tests;

/// <summary>
///     Mecanismo de métricas customizadas (<see cref="BaseMetrics{T}" />): counters observáveis e
///     histogramas emitem medições com a tag <c>application</c>, captadas por um <see cref="MeterListener" />
///     — comprovando que qualquer métrica custom é observável (e, por <c>AddMeter("*")</c>, exportada).
/// </summary>
public sealed class BaseMetricsTests
{
    private const string MeterName = "Aedis.Tests.Metrics";

    private static IMeterFactory MeterFactory() =>
        new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    [Fact]
    public void Counter_observavel_acumula_por_tag_e_inclui_application() {
        var longMeasurements = new List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => {
            if (instrument.Meter.Name == MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            longMeasurements.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();

        var metrics = new TestMetrics(MeterFactory());
        metrics.Processed("csv");
        metrics.Processed("csv");
        metrics.Processed("xml");

        listener.RecordObservableInstruments();

        longMeasurements.Should().Contain(m =>
            m.Name == "test_processed_total" && m.Value == 2
            && m.Tags.Any(t => t.Key == "type" && (string?)t.Value == "csv")
            && m.Tags.Any(t => t.Key == "application"));
        longMeasurements.Should().Contain(m =>
            m.Name == "test_processed_total" && m.Value == 1
            && m.Tags.Any(t => t.Key == "type" && (string?)t.Value == "xml"));
    }

    [Fact]
    public void Histograma_registra_valor_com_application() {
        var doubleMeasurements = new List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => {
            if (instrument.Meter.Name == MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            doubleMeasurements.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();

        var metrics = new TestMetrics(MeterFactory());
        metrics.RecordDuration(1.5);

        doubleMeasurements.Should().ContainSingle(m =>
            m.Name == "test_duration_seconds" && m.Value == 1.5
            && m.Tags.Any(t => t.Key == "application"));
    }

    private sealed class TestMetrics : BaseMetrics<TestMetrics>
    {
        private Histogram<double> _duration = null!;
        private string _processed = null!;

        public TestMetrics(IMeterFactory meterFactory) : base(meterFactory, MeterName) { }

        protected override void ConfigureMetrics() {
            _processed = CreateObservableCounter("test_processed_total", "Itens processados");
            _duration = CreateHistogram("test_duration_seconds", "Duração", "s");
        }

        public void Processed(string type) => IncrementCounter(_processed, 1, "type", type);

        public void RecordDuration(double seconds) => RecordHistogram(_duration, seconds);
    }
}
