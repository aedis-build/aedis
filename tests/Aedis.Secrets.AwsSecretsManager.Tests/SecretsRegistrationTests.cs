using Aedis.Secrets;
using Aedis.Secrets.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Secrets.AwsSecretsManager.Tests;

/// <summary>
///     <c>AddAedisAwsSecretsManager()</c> sem AWS: vincula as opções (incluindo o TTL de cache da seção
///     <c>Secrets</c>), expõe <see cref="ISecretsProvider" /> decorado com cache e registra o health check
///     <c>secrets</c> com a tag <c>ready</c>. A construção do cliente AWS é preguiçosa — não toca a rede.
/// </summary>
public sealed class SecretsRegistrationTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["AwsSecretsManager:Region"] = "us-east-1",
            ["Secrets:CacheTtl"] = "00:10:00"
        }).Build();

    [Fact]
    public void Registra_provider_com_cache() {
        var provider = new ServiceCollection().AddLogging().AddAedisAwsSecretsManager(Config()).BuildServiceProvider();

        provider.GetRequiredService<ISecretsProvider>().Should().BeOfType<CachingSecretsProvider>();
        provider.GetRequiredService<IOptions<SecretsCachingOptions>>().Value.CacheTtl
            .Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Cache_desligado_expoe_o_provider_cru() {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AwsSecretsManager:Region"] = "us-east-1",
            ["Secrets:CacheEnabled"] = "false"
        }).Build();

        var provider = new ServiceCollection().AddLogging().AddAedisAwsSecretsManager(config).BuildServiceProvider();

        provider.GetRequiredService<ISecretsProvider>().Should().BeOfType<AwsSecretsManagerProvider>();
    }

    [Fact]
    public void Registra_health_check_secrets_como_ready() {
        var provider = new ServiceCollection().AddLogging().AddAedisAwsSecretsManager(Config()).BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "secrets").Subject;

        registration.Tags.Should().Contain("ready");
    }
}
