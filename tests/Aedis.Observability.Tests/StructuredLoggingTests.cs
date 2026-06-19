using Aedis.Observability.Serilog;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Aedis.Observability.Tests;

/// <summary>
///     O log padrão do Aedis é estruturado (JSON compacto): cada evento
///     carrega <c>application</c> (mesma identidade das métricas), <c>LogType</c> e os campos nomeados do
///     template, prontos para filtrar/correlacionar no backend.
/// </summary>
public sealed class StructuredLoggingTests
{
    [Fact]
    public void Evento_carrega_application_LogType_e_campos_nomeados() {
        var captured = new List<LogEvent>();

        var loggerConfiguration = new LoggerConfiguration();
        AedisSerilog.Configure(loggerConfiguration, new ConfigurationBuilder().Build());
        loggerConfiguration.WriteTo.Sink(new CaptureSink(captured));

        using (var logger = loggerConfiguration.CreateLogger())
            logger.Information("Pedido {OrderId} processado", 42);

        var logEvent = captured.Should().ContainSingle().Subject;
        logEvent.Properties.Should().ContainKey("application");
        logEvent.Properties.Should().ContainKey("LogType");
        logEvent.Properties.Should().ContainKey("OrderId");
        logEvent.Properties["OrderId"].ToString().Should().Be("42");
    }

    [Fact]
    public void Ruido_de_health_e_favicon_e_filtrado() {
        var captured = new List<LogEvent>();

        var loggerConfiguration = new LoggerConfiguration();
        AedisSerilog.Configure(loggerConfiguration, new ConfigurationBuilder().Build());
        loggerConfiguration.WriteTo.Sink(new CaptureSink(captured));

        using (var logger = loggerConfiguration.CreateLogger()) {
            logger.ForContext("RequestPath", "/health/ready").Information("probe");
            logger.ForContext("RequestPath", "/api/orders").Information("real");
        }

        captured.Should().ContainSingle();
        captured[0].Properties["RequestPath"].ToString().Should().Contain("/api/orders");
    }

    private sealed class CaptureSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
