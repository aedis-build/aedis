using Aedis.AI.Abstractions;
using Aedis.AI.ManagedAgents;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.AI.ManagedAgents.Tests;

/// <summary>
///     Registro de DI: <c>AddAedisAI().WithManagedAgents(configuration)</c> vincula <c>AI:ManagedAgents</c> e resolve
///     o <see cref="IAgentEngine" /> como <see cref="ManagedAgentsEngine" />.
/// </summary>
public class ManagedAgentsRegistrationTests
{
    [Fact]
    public void Registra_o_motor_e_vincula_a_secao() {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:ManagedAgents:ApiKey"] = "sk-teste" })
            .Build();
        var services = new ServiceCollection().AddLogging();

        services.AddAedisAI().WithManagedAgents(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAgentEngine>().Should().BeOfType<ManagedAgentsEngine>();
        provider.GetRequiredService<IOptions<ManagedAgentsOptions>>().Value.ApiKey.Should().Be("sk-teste");
    }
}
