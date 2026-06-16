using Aedis.Messaging.Abstractions;
using Aedis.Messaging.AzureServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Messaging.AzureServiceBus.Tests;

/// <summary>
///     Partes puras do provider Azure Service Bus (sem namespace real): roteamento topic/queue,
///     normalização de nomes e a extensão de DI.
/// </summary>
public sealed class ServiceBusUnitTests
{
    private const string FakeConnection =
        "Endpoint=sb://aedis-test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=dmFsdWU=";

    [Theory]
    [InlineData("orders", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    public void IsTopic_trata_exchange_vazio_como_queue(string? exchange, bool expected) {
        ServiceBusBaseService.IsTopic(exchange).Should().Be(expected);
    }

    [Theory]
    [InlineData("My Topic", "my-topic")]
    [InlineData("Order_Created", "order-created")]
    [InlineData("Already-Lower", "already-lower")]
    public void NormalizeName_minuscula_e_troca_separadores(string input, string expected) {
        ServiceBusBaseService.NormalizeName(input).Should().Be(expected);
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["ServiceBus:ConnectionString"] = FakeConnection,
            ["ServiceBus:MaxConcurrentCalls"] = "4"
        }).Build();

    [Fact]
    public void AddAedisAzureServiceBus_vincula_options_e_registra_broker_keyed() {
        var services = new ServiceCollection().AddLogging().AddAedisAzureServiceBus(Config());

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.MaxConcurrentCalls.Should().Be(4);

        services.Should().Contain(d => d.ServiceType == typeof(IMessageBrokerService));
        services.Should().Contain(d => d.ServiceType == typeof(IMessageBrokerService)
                                       && d.IsKeyedService && Equals(d.ServiceKey, "azureservicebus"));
    }

    [Fact]
    public void AddAedisAzureServiceBus_registra_health_check_como_ready() {
        var provider = new ServiceCollection().AddLogging().AddAedisAzureServiceBus(Config()).BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "azureservicebus").Subject;

        registration.Tags.Should().Contain("ready");
    }
}
