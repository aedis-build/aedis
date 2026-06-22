using Aedis.Observability.Serilog;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Aedis.Observability.Tests;

/// <summary>
///     Garante que o pipeline de log do Aedis ofusca segredos e PII <strong>antes</strong> de qualquer sink:
///     segredos viram máscara total, PII mantém só os últimos dígitos, campos não-sensíveis passam intactos —
///     em propriedades de topo, objetos destruturados, dicionários e via <c>[SensitiveData]</c>.
/// </summary>
public sealed class RedactionTests {
    private static (Logger Logger, List<LogEvent> Events) Build(Dictionary<string, string?>? config = null) {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build();
        var captured = new List<LogEvent>();
        var loggerConfiguration = new LoggerConfiguration();
        AedisSerilog.Configure(loggerConfiguration, configuration);
        loggerConfiguration.WriteTo.Sink(new CaptureSink(captured));
        return (loggerConfiguration.CreateLogger(), captured);
    }

    private static string? Scalar(LogEventPropertyValue? value) => (value as ScalarValue)?.Value?.ToString();

    private static string? Prop(LogEventPropertyValue structure, string name) =>
        Scalar(((StructureValue)structure).Properties.First(p => p.Name == name).Value);

    [Fact]
    public void Segredo_por_nome_vira_mascara_total() {
        var (logger, events) = Build();

        using (logger) {
            logger.Information("login {Authorization} {Token}", "Bearer abc.def.ghi", "supersecret9999");
        }

        Scalar(events[0].Properties["Authorization"]).Should().Be("***");
        Scalar(events[0].Properties["Token"]).Should().Be("***");
    }

    [Fact]
    public void Pii_por_nome_mantem_apenas_os_ultimos_quatro() {
        var (logger, events) = Build();

        using (logger) {
            logger.Information("doc {Cpf}", "12345678901");
        }

        Scalar(events[0].Properties["Cpf"]).Should().Be("***8901");
    }

    [Fact]
    public void Campo_nao_sensivel_passa_intacto() {
        var (logger, events) = Build();

        using (logger) {
            logger.Information("produto {Name}", "Widget");
        }

        Scalar(events[0].Properties["Name"]).Should().Be("Widget");
    }

    [Fact]
    public void Objeto_destruturado_ofusca_campos_aninhados() {
        var (logger, events) = Build();

        using (logger) {
            logger.Information("req {@Payload}", new { Password = "hunter2pass", Cpf = "12345678901", Product = "Widget" });
        }

        var payload = events[0].Properties["Payload"];
        Prop(payload, "Password").Should().Be("***");
        Prop(payload, "Cpf").Should().Be("***8901");
        Prop(payload, "Product").Should().Be("Widget");
    }

    [Fact]
    public void Atributo_SensitiveData_ofusca_campo_ambiguo() {
        var (logger, events) = Build();

        using (logger) {
            logger.Information("cliente {@Customer}", new Customer { FullName = "Maria Silva", City = "SP" });
        }

        var customer = events[0].Properties["Customer"];
        Prop(customer, "FullName").Should().Be("***ilva");
        Prop(customer, "City").Should().Be("SP");
    }

    [Fact]
    public void Chave_sensivel_em_dicionario_e_ofuscada() {
        var (logger, events) = Build();

        using (logger) {
            logger.Information("headers {@Headers}", new Dictionary<string, string> {
                ["Authorization"] = "Bearer abc",
                ["Accept"] = "application/json"
            });
        }

        var headers = (DictionaryValue)events[0].Properties["Headers"];
        Scalar(headers.Elements.First(e => (string)e.Key.Value! == "Authorization").Value).Should().Be("***");
        Scalar(headers.Elements.First(e => (string)e.Key.Value! == "Accept").Value).Should().Be("application/json");
    }

    [Fact]
    public void Desligar_via_config_preserva_o_valor() {
        var (logger, events) = Build(new Dictionary<string, string?> { ["Logging:Redaction:Enabled"] = "false" });

        using (logger) {
            logger.Information("login {Token}", "supersecret9999");
        }

        Scalar(events[0].Properties["Token"]).Should().Be("supersecret9999");
    }

    private sealed class Customer {
        [SensitiveData]
        public string FullName { get; set; } = string.Empty;

        public string City { get; set; } = string.Empty;
    }

    private sealed class CaptureSink(List<LogEvent> events) : ILogEventSink {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
