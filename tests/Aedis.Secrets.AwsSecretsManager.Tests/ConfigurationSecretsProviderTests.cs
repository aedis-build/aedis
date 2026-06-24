using Aedis.Secrets;
using Aedis.Secrets.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Secrets.AwsSecretsManager.Tests;

/// <summary>
///     <see cref="ConfigurationSecretsProvider" /> (fallback local): lê segredos do <see cref="IConfiguration" />
///     sob o prefixo configurado (ou da raiz), devolve <c>null</c> em ausência e é resolvido pelo registro de DI.
/// </summary>
public sealed class ConfigurationSecretsProviderTests
{
    [Fact]
    public async Task Le_segredo_sob_o_prefixo_padrao() {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Secrets:DbPassword"] = "p@ss"
        }).Build();
        var provider = new ConfigurationSecretsProvider(config);

        (await provider.GetSecretAsync("DbPassword")).Should().Be("p@ss");
        var metadata = await provider.GetSecretWithMetadataAsync("DbPassword");
        metadata!.Value.Should().Be("p@ss");
        metadata.Version.Should().BeNull();
    }

    [Fact]
    public async Task Prefixo_vazio_le_da_raiz() {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ApiKey"] = "abc"
        }).Build();
        var provider = new ConfigurationSecretsProvider(config, sectionPrefix: null);

        (await provider.GetSecretAsync("ApiKey")).Should().Be("abc");
    }

    [Fact]
    public async Task Inexistente_devolve_null() {
        var provider = new ConfigurationSecretsProvider(new ConfigurationBuilder().Build());

        (await provider.GetSecretAsync("x")).Should().BeNull();
        (await provider.GetSecretWithMetadataAsync("x")).Should().BeNull();
    }

    [Fact]
    public void AddAedisConfigurationSecrets_registra_o_provider() {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Secrets:K"] = "v" }).Build();

        var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddAedisConfigurationSecrets()
            .BuildServiceProvider();

        provider.GetRequiredService<ISecretsProvider>().Should().BeOfType<ConfigurationSecretsProvider>();
    }
}
