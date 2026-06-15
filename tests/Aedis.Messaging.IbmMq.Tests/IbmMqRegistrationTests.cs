using Aedis.Messaging.Abstractions;
using Aedis.Messaging.IbmMq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Messaging.IbmMq.Tests;

/// <summary>
///     Verifica a extensão de DI <c>AddAedisIbmMq()</c> — registro de options, broker (padrão e keyed
///     <c>"ibmmq"</c>) e health check <c>ready</c>. Não resolve o broker (que conectaria), apenas valida
///     options, registros e a inspeção dos health checks.
/// </summary>
public sealed class IbmMqRegistrationTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["IBMMQ:QueueManager"] = "QM1",
            ["IBMMQ:Channel"] = "DEV.APP.SVRCONN",
            ["IBMMQ:ConnectionNameList"] = "localhost(1414)",
            ["IBMMQ:UserId"] = "app",
            ["IBMMQ:Password"] = "passw0rd",
            ["IBMMQ:MessageType"] = "Request",
            ["IBMMQ:EnableReports"] = "true",
            ["IBMMQ:Reports:Coa"] = "true",
            ["IBMMQ:Reports:Cod"] = "true"
        }).Build();

    [Fact]
    public void AddAedisIbmMq_vincula_options_incluindo_a_lista_de_ativacao() {
        var provider = new ServiceCollection().AddLogging().AddAedisIbmMq(Config()).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<IbmMqOptions>>().Value;

        options.QueueManager.Should().Be("QM1");
        options.MessageType.Should().Be(MqMessageType.Request);
        options.Reports.Coa.Should().BeTrue();
        options.Reports.Cod.Should().BeTrue();
        options.Reports.Exception.Should().BeFalse("só o que foi configurado é ativado");
    }

    [Fact]
    public void AddAedisIbmMq_registra_o_broker_padrao_e_keyed() {
        var services = new ServiceCollection().AddLogging().AddAedisIbmMq(Config());

        services.Should().Contain(d => d.ServiceType == typeof(IMessageBrokerService));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IMessageBrokerService) && d.IsKeyedService && Equals(d.ServiceKey, "ibmmq"));
    }

    [Fact]
    public void AddAedisIbmMq_registra_health_check_ibmmq_como_ready() {
        var provider = new ServiceCollection().AddLogging().AddAedisIbmMq(Config()).BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "ibmmq").Subject;

        registration.Tags.Should().Contain("ready");
    }
}
