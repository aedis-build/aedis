using Aedis.Observability.Serilog;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Aedis.Observability.Tests;

/// <summary>Redação e níveis configurados por appsettings: estratégias, hash, chaves extras, sequências e MinimumLevel.</summary>
public sealed class RedactionConfigurationTests
{
    private sealed class ListSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static (Logger Logger, List<LogEvent> Events) Build(Dictionary<string, string?> config) {
        var captured = new List<LogEvent>();
        var loggerConfiguration = new LoggerConfiguration();
        AedisSerilog.Configure(loggerConfiguration, Config(config));
        loggerConfiguration.WriteTo.Sink(new ListSink(captured));
        return (loggerConfiguration.CreateLogger(), captured);
    }

    private static string? Scalar(LogEventPropertyValue? value) => (value as ScalarValue)?.Value?.ToString();

    [Fact]
    public void FromConfiguration_le_todas_as_chaves() {
        var options = RedactionOptions.FromConfiguration(Config(new Dictionary<string, string?> {
            ["Logging:Redaction:Enabled"] = "false",
            ["Logging:Redaction:Placeholder"] = "[oculto]",
            ["Logging:Redaction:KeepLast"] = "2",
            ["Logging:Redaction:SecretStrategy"] = "Hash",
            ["Logging:Redaction:PiiStrategy"] = "Mask",
            ["Logging:Redaction:HashKey"] = "k",
            ["Logging:Redaction:MaskSecretsInText"] = "false",
            ["Logging:Redaction:AdditionalSecretKeys:0"] = "X-Signature",
            ["Logging:Redaction:AdditionalPiiKeys:0"] = "Mother_Name"
        }));

        options.Enabled.Should().BeFalse();
        options.Placeholder.Should().Be("[oculto]");
        options.KeepLast.Should().Be(2);
        options.SecretStrategy.Should().Be(RedactionStrategy.Hash);
        options.PiiStrategy.Should().Be(RedactionStrategy.Mask);
        options.HashKey.Should().Be("k");
        options.MaskSecretsInText.Should().BeFalse();
        options.SecretKeys.Should().Contain("xsignature");
        options.PiiKeys.Should().Contain("mothername");
    }

    [Fact]
    public void FromConfiguration_ignora_valores_invalidos_e_Inherit() {
        var options = RedactionOptions.FromConfiguration(Config(new Dictionary<string, string?> {
            ["Logging:Redaction:KeepLast"] = "-1",
            ["Logging:Redaction:SecretStrategy"] = "Inherit",
            ["Logging:Redaction:PiiStrategy"] = "nope"
        }));

        options.KeepLast.Should().Be(4);
        options.SecretStrategy.Should().Be(RedactionStrategy.Mask);
        options.PiiStrategy.Should().Be(RedactionStrategy.Partial);
    }

    [Fact]
    public void Estrategia_Hash_usa_HMAC_com_a_chave_e_cai_no_placeholder_sem_chave() {
        var redactor = new Redactor(new RedactionOptions { SecretStrategy = RedactionStrategy.Hash, HashKey = "k" });
        var semChave = new Redactor(new RedactionOptions { SecretStrategy = RedactionStrategy.Hash });

        var hashed = redactor.Apply("segredo", RedactionStrategy.Hash);
        hashed.Should().StartWith("hash:").And.HaveLength(21);
        redactor.Apply("segredo", RedactionStrategy.Hash).Should().Be(hashed, "o hash é determinístico");
        semChave.Apply("segredo", RedactionStrategy.Hash).Should().Be("***");
        redactor.Apply(null, RedactionStrategy.Mask).Should().Be("***");
        redactor.Classify("qualquer").Should().Be(Redactor.Classification.None);
    }

    [Fact]
    public void Sequencia_de_estruturas_tem_os_segredos_redigidos() {
        var (logger, events) = Build([]);
        using (logger) {
            logger.Information("lote {@Items}", new[] { new { Password = "p1", Name = "a" }, new { Password = "p2", Name = "b" } });
        }

        var sequence = (SequenceValue)events[0].Properties["Items"];
        sequence.Elements.Should().AllSatisfy(e =>
            Scalar(((StructureValue)e).Properties.First(p => p.Name == "Password").Value).Should().Be("***"));
    }

    [Fact]
    public void MinimumLevel_e_overrides_vem_da_configuracao() {
        var (logger, events) = Build(new Dictionary<string, string?> {
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Serilog:MinimumLevel:Override:Aedis.Verbose"] = "Debug"
        });
        using (logger) {
            logger.Information("descartada");
            logger.Warning("mantida");
            logger.ForContext(Constants.SourceContextPropertyName, "Aedis.Verbose.Thing").Debug("mantida por override");
        }

        events.Select(e => e.MessageTemplate.Text).Should().Equal("mantida", "mantida por override");
    }

    [Fact]
    public void Sink_OTLP_e_configurado_so_com_endpoint_e_logs_habilitados() {
        var withSink = () => Build(new Dictionary<string, string?> {
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4318/v1/logs",
            ["Telemetry:OtlpApiKey"] = "k"
        }).Logger.Dispose();
        var disabled = () => Build(new Dictionary<string, string?> {
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4318/v1/logs",
            ["Telemetry:EnableLogs"] = "false"
        }).Logger.Dispose();

        withSink.Should().NotThrow();
        disabled.Should().NotThrow();
    }
}
