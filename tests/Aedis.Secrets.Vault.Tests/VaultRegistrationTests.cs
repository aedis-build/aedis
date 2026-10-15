using Aedis.Secrets;
using Aedis.Secrets.Abstractions;
using Aedis.Secrets.Vault;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Secrets.Vault.Tests;

/// <summary>
///     <c>AddAedisVault()</c> sem Vault: vincula as opções (incluindo o TTL de cache da seção <c>Secrets</c>),
///     expõe <see cref="ISecretsProvider" /> decorado com cache, registra o health check <c>secrets</c> com a
///     tag <c>ready</c> e exige <c>Address</c>/<c>Token</c>. A construção do cliente é preguiçosa.
/// </summary>
public sealed class VaultRegistrationTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["Vault:Address"] = "http://localhost:8200",
            ["Vault:Token"] = "root",
            ["Secrets:CacheTtl"] = "00:10:00"
        }).Build();

    [Fact]
    public void Registra_provider_com_cache() {
        var provider = new ServiceCollection().AddLogging().AddAedisVault(Config()).BuildServiceProvider();

        provider.GetRequiredService<ISecretsProvider>().Should().BeOfType<CachingSecretsProvider>();
        provider.GetRequiredService<IOptions<SecretsCachingOptions>>().Value.CacheTtl
            .Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Registra_health_check_secrets_como_ready() {
        var provider = new ServiceCollection().AddLogging().AddAedisVault(Config()).BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "secrets").Subject;

        registration.Tags.Should().Contain("ready");
    }

    [Fact]
    public void Address_ou_token_ausente_falha_na_validacao() {
        var provider = new ServiceCollection().AddLogging()
            .AddAedisVault(new ConfigurationBuilder().Build()).BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<VaultOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData("Kubernetes", "Vault:KubernetesRole")]
    [InlineData("AppRole", "Vault:AppRoleId")]
    public void Metodo_de_autenticacao_exige_as_suas_credenciais(string auth, string missing) {
        var options = new VaultOptions { Address = "http://localhost:8200", Auth = Enum.Parse<VaultAuthMethod>(auth) };

        options.Validate().Should().Contain(missing);
    }

    [Fact]
    public void Kubernetes_troca_o_token_projetado_da_ServiceAccount() {
        var tokenFile = Path.GetTempFileName();
        File.WriteAllText(tokenFile, "eyJhbGciOi.jwt.projetado\n");
        try {
            var options = new VaultOptions {
                Address = "http://localhost:8200", Auth = VaultAuthMethod.Kubernetes, KubernetesRole = "orders-api", KubernetesTokenPath = tokenFile
            };

            options.Validate().Should().BeNull();
            var act = () => VaultSecretsProvider.Create(options);
            act.Should().NotThrow("o cliente é preguiçoso: só autentica na primeira chamada");
        }
        finally {
            File.Delete(tokenFile);
        }
    }

    [Fact]
    public void AppRole_monta_cliente_sem_token_estatico() {
        var options = new VaultOptions { Address = "http://localhost:8200", Auth = VaultAuthMethod.AppRole, AppRoleId = "role", AppRoleSecretId = "secret" };

        options.Validate().Should().BeNull();
        var act = () => VaultSecretsProvider.Create(options);
        act.Should().NotThrow();
    }
}
