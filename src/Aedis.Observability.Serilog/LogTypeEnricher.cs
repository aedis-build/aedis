using Serilog.Core;
using Serilog.Events;

namespace Aedis.Observability.Serilog;

/// <summary>Enriquecedor que adiciona a propriedade <c>LogType</c> (ex.: <c>Application</c>) a cada evento.</summary>
public sealed class LogTypeEnricher(string logType) : ILogEventEnricher
{
    private readonly LogEventProperty _property = new("LogType", new ScalarValue(logType));

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
        logEvent.AddPropertyIfAbsent(_property);
}
