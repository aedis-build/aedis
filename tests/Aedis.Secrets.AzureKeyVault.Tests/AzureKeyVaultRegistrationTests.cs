using Aedis.Secrets;
using Aedis.Secrets.Abstractions;
using Aedis.Secrets.AzureKeyVault;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Secrets.AzureKeyVault.Tests;

/// <summary>
///     <c>AddAedisAzureKeyVault()</c> sem Azure: vincula as opções (incluindo o TTL de cache da seção
///     <c>Secrets</c>), expõe <see cref="ISecretsProvider" /> decorado com cache, registra o health check
///     <c>secrets</c> com a tag <c>ready</c> e exige <c>VaultUri</c>. A construção do cliente é preguiçosa.
/// </summary>
public sealed class AzureKeyVaultRegistrationTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["AzureKeyVault:VaultUri"] = "https://cofre.vault.azure.net/",
            ["Secrets:CacheTtl"] = "00:10:00"
        }).Build();

    [Fact]
    public void Registra_provider_com_cache() {
        var provider = new ServiceCollection().AddLogging().AddAedisAzureKeyVault(Config()).BuildServiceProvider();

        provider.GetRequiredService<ISecretsProvider>().Should().BeOfType<CachingSecretsProvider>();
        provider.GetRequiredService<IOptions<SecretsCachingOptions>>().Value.CacheTtl
            .Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Registra_health_check_secrets_como_ready() {
        var provider = new ServiceCollection().AddLogging().AddAedisAzureKeyVault(Config()).BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "secrets").Subject;

        registration.Tags.Should().Contain("ready");
    }

    [Fact]
    public void VaultUri_ausente_falha_na_validacao() {
        var provider = new ServiceCollection().AddLogging()
            .AddAedisAzureKeyVault(new ConfigurationBuilder().Build()).BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<AzureKeyVaultOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }
}
