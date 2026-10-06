using Aedis.Observability.Serilog;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Aedis.Observability.Tests;

/// <summary>
///     Redaction de segredos em <strong>texto livre</strong>, onde a classificação por nome de campo não
///     alcança: o <c>TextRedactor</c> mascara Bearer/Basic, pares <c>password=</c>/<c>token:</c>, blocos PEM e
///     access keys; o <c>RedactingSink</c> reescreve o template da mensagem antes dos sinks reais; e o
///     enricher aplica o mesmo redactor a propriedades string cujo nome não é sensível.
/// </summary>
public sealed class TextRedactionTests {
    private static TextRedactor Redactor() => new(new RedactionOptions());

    private static (Logger Logger, List<LogEvent> Events) BuildPipeline(Dictionary<string, string?>? config = null) {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build();
        var captured = new List<LogEvent>();
        var loggerConfiguration = new LoggerConfiguration();
        AedisSerilog.Configure(loggerConfiguration, configuration);
        loggerConfiguration.WriteTo.Sink(new ListSink(captured));
        return (loggerConfiguration.CreateLogger(), captured);
    }

    private static string? Scalar(LogEventPropertyValue? value) => (value as ScalarValue)?.Value?.ToString();

    [Theory]
    [InlineData("Authorization: Bearer abc.def.ghi", "Authorization: Bearer ***")]
    [InlineData("header Basic dXNlcjpwYXNz", "header Basic ***")]
    [InlineData("password=hunter2 user=bob", "password=*** user=bob")]
    [InlineData("api_key: \"xyz-123\" ok", "api_key: *** ok")]
    [InlineData("client_secret = s3cr3t;next", "client_secret = ***;next")]
    [InlineData("key AKIAIOSFODNN7EXAMPLE usada", "key *** usada")]
    public void Segredo_em_texto_livre_e_mascarado(string entrada, string esperado) {
        Redactor().Redact(entrada).Should().Be(esperado);
    }

    [Fact]
    public void Bloco_de_chave_privada_e_mascarado_por_inteiro() {
        var pem = "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBg\n-----END PRIVATE KEY-----";

        var result = Redactor().Redact("chave: " + pem);

        result.Should().Be("chave: ***");
    }

    [Fact]
    public void Texto_sem_segredo_volta_a_mesma_instancia() {
        const string texto = "pedido 42 processado";

        Redactor().Redact(texto).Should().BeSameAs(texto);
    }

    [Fact]
    public void Sink_reescreve_o_template_da_mensagem() {
        var captured = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .WriteTo.Sink(new RedactingSink(new ListSink(captured), new RedactionOptions()))
            .CreateLogger();

        logger.Information("token=abc123 recebido para {Pedido}", 42);

        captured.Should().ContainSingle();
        captured[0].MessageTemplate.Text.Should().Be("token=*** recebido para {Pedido}");
        captured[0].RenderMessage().Should().Be("token=*** recebido para 42");
        Scalar(captured[0].Properties["Pedido"]).Should().Be("42");
    }

    [Fact]
    public void Sink_entrega_o_mesmo_evento_quando_nao_ha_segredo() {
        var captured = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .WriteTo.Sink(new RedactingSink(new ListSink(captured), new RedactionOptions()))
            .CreateLogger();

        logger.Information("pedido {Pedido} processado", 42);

        captured[0].MessageTemplate.Text.Should().Be("pedido {Pedido} processado");
    }

    [Fact]
    public void Propriedade_string_com_bearer_e_mascarada_mesmo_sem_nome_sensivel() {
        var (logger, events) = BuildPipeline();

        using (logger) {
            logger.Information("cabeçalho {Header}", "Bearer abc.def.ghi");
        }

        Scalar(events[0].Properties["Header"]).Should().Be("Bearer ***");
    }

    [Fact]
    public void Mascaramento_em_texto_pode_ser_desligado_por_configuracao() {
        var (logger, events) = BuildPipeline(new Dictionary<string, string?> {
            ["Logging:Redaction:MaskSecretsInText"] = "false"
        });

        using (logger) {
            logger.Information("cabeçalho {Header}", "Bearer abc.def.ghi");
        }

        Scalar(events[0].Properties["Header"]).Should().Be("Bearer abc.def.ghi");
    }

    private sealed class ListSink(List<LogEvent> events) : ILogEventSink {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
