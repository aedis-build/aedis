using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Aedis.Core.Utils;

namespace Aedis.Observability.Abstractions;

/// <summary>
///     Base para métricas customizadas sobre <see cref="System.Diagnostics.Metrics" />, com template method
///     (<see cref="ConfigureMetrics" />) e helpers para counters observáveis e histogramas. Cada instância
///     cria seu próprio <see cref="Meter" /> — coletado automaticamente pela telemetria do Aedis
///     (<c>AddMeter("*")</c>), então as métricas fluem por default para qualquer backend OTLP. Toda métrica
///     leva a tag <c>application</c> (nome da aplicação) para identificação no backend.
/// </summary>
/// <typeparam name="TMetrics">A própria classe de métricas (define o nome do Meter por convenção).</typeparam>
public abstract class BaseMetrics<TMetrics> : IDisposable, IAsyncDisposable
    where TMetrics : BaseMetrics<TMetrics>
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long>> _counterStates = new();
    private readonly Meter _meter;
    private bool _disposed;

    protected BaseMetrics(IMeterFactory meterFactory, string? meterName = null) {
        ApplicationName = ApplicationInfo.Name;
        _meter = meterFactory.Create(meterName ?? typeof(TMetrics).Namespace ?? "Aedis.Application");
        ConfigureMetrics();
    }

    private string ApplicationName { get; }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Template method: a classe derivada configura suas métricas aqui usando os helpers.</summary>
    protected abstract void ConfigureMetrics();

    /// <summary>
    ///     Cria um ObservableCounter com estado acumulado thread-safe. Devolve a chave usada em
    ///     <see cref="IncrementCounter" />/<see cref="AddToCounter" />.
    /// </summary>
    protected string CreateObservableCounter(string name, string description, string? unit = null) {
        var state = new ConcurrentDictionary<string, long>();
        _counterStates[name] = state;
        _meter.CreateObservableCounter(name, () => GetMeasurements(state), unit, description);
        return name;
    }

    /// <summary>Cria um histograma para durações/distribuições.</summary>
    protected Histogram<double> CreateHistogram(string name, string description, string unit = "1") =>
        _meter.CreateHistogram<double>(name, unit, description);

    /// <summary>Incrementa o counter (tags como pares "k","v","k","v").</summary>
    protected void IncrementCounter(string stateKey, long value = 1, params string[] tags) =>
        AddToCounter(stateKey, value, tags);

    /// <summary>Adiciona ao counter um valor acumulado (tags como pares "k","v").</summary>
    protected void AddToCounter(string stateKey, long value, params string[] tags) {
        if (!_counterStates.TryGetValue(stateKey, out var state))
            throw new InvalidOperationException(
                $"Counter '{stateKey}' não encontrado. Chame CreateObservableCounter primeiro.");

        state.AddOrUpdate(BuildKey(tags), value, (_, current) => current + value);
    }

    /// <summary>Registra um valor no histograma (sempre com a tag <c>application</c>).</summary>
    protected void RecordHistogram(Histogram<double> histogram, double value,
        params (string Key, object? Value)[] tags) {
        var tagArray = new KeyValuePair<string, object?>[] { new("application", ApplicationName) }
            .Concat(tags.Select(t => new KeyValuePair<string, object?>(t.Key, t.Value)))
            .ToArray();
        histogram.Record(value, tagArray);
    }

    protected static string BuildKey(params string[] values) => string.Join("|", values);

    protected virtual void Dispose(bool disposing) {
        if (_disposed) return;
        if (disposing) {
            _meter.Dispose();
            _counterStates.Clear();
        }

        _disposed = true;
    }

    protected virtual ValueTask DisposeAsyncCore() {
        _meter.Dispose();
        _counterStates.Clear();
        return ValueTask.CompletedTask;
    }

    private KeyValuePair<string, object?>[] ParseKey(string key) {
        var parts = key.Split('|');
        var tags = new List<KeyValuePair<string, object?>> { new("application", ApplicationName) };
        for (var i = 0; i + 1 < parts.Length; i += 2)
            tags.Add(new KeyValuePair<string, object?>(parts[i], parts[i + 1]));
        return tags.ToArray();
    }

    private IEnumerable<Measurement<long>> GetMeasurements(ConcurrentDictionary<string, long> state) {
        if (state.IsEmpty) {
            yield return new Measurement<long>(0, new KeyValuePair<string, object?>("application", ApplicationName));
            yield break;
        }

        foreach (var kvp in state)
            yield return new Measurement<long>(kvp.Value, ParseKey(kvp.Key));
    }
}
