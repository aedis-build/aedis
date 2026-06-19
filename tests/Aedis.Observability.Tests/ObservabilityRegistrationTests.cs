using Aedis.Observability.Serilog;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Aedis.Observability.Tests;

/// <summary>
///     Registro de DI da telemetria OTLP e do Serilog, e o enriquecedor <c>LogType</c>.
/// </summary>
public sealed class ObservabilityRegistrationTests
{
    [Fact]
    public void AddAedisTelemetry_registra_meterfactory_e_health_check_ready() {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4317"
        }).Build();

        var provider = new ServiceCollection().AddLogging().AddAedisTelemetry(config).BuildServiceProvider();

        provider.GetService<System.Diagnostics.Metrics.IMeterFactory>().Should().NotBeNull();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "otlp").Subject;
        registration.Tags.Should().Contain("ready");
    }

    [Fact]
    public void AddAedisSerilog_registra_o_provider_de_logging() {
        var provider = new ServiceCollection()
            .AddAedisSerilog(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        provider.GetRequiredService<ILoggerFactory>().CreateLogger("x").Should().NotBeNull();
    }

    [Fact]
    public void LogTypeEnricher_adiciona_a_propriedade_LogType() {
        var enricher = new LogTypeEnricher("Event");
        var template = new MessageTemplateParser().Parse("teste");
        var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, template, []);

        enricher.Enrich(logEvent, null!); // o enricher usa uma propriedade pré-construída; ignora a factory

        logEvent.Properties.Should().ContainKey("LogType");
        logEvent.Properties["LogType"].ToString().Should().Contain("Event");
    }
}
