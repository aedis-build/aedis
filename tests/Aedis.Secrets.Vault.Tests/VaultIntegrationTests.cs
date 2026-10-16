using Aedis.Secrets.Abstractions;
using Aedis.Secrets.Vault;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aedis.Secrets.Vault.Tests;

/// <summary>
///     Leitura de segredos contra um HashiCorp Vault real em modo dev (Testcontainers). Prova o caminho KV
///     v2 — valor, versão e <c>null</c> em segredo inexistente. Integração opt-in: ligue com a env
///     <c>AEDIS_VAULT_IT=1</c>.
/// </summary>
public sealed class VaultIntegrationTests : IClassFixture<VaultIntegrationTests.VaultFixture>
{
    private const string RootToken = "root";
    private readonly VaultFixture _fixture;

    public VaultIntegrationTests(VaultFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Le_segredo_e_metadados() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_VAULT_IT=1 para rodar a integração Vault.");
        var name = $"aedis-it-{Guid.NewGuid():N}";
        await _fixture.WriteSecretAsync(name, "v");
        var provider = _fixture.Provider();

        (await provider.GetSecretAsync(name)).Should().Be("v");

        var metadata = await provider.GetSecretWithMetadataAsync(name);
        metadata.Should().NotBeNull();
        metadata!.Value.Should().Be("v");
        metadata.Version.Should().Be("1");
    }

    [SkippableFact]
    public async Task Segredo_inexistente_devolve_null_e_required_lanca() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_VAULT_IT=1 para rodar a integração Vault.");
        var provider = _fixture.Provider();
        var missing = $"aedis-missing-{Guid.NewGuid():N}";

        (await provider.GetSecretAsync(missing)).Should().BeNull();

        var act = () => provider.GetRequiredSecretAsync(missing);
        await act.Should().ThrowAsync<SecretNotFoundException>();
    }

    /// <summary>Grava, lê de volta, apaga (inclusive um inexistente) e confirma o health check contra o Vault real.</summary>
    [SkippableFact]
    public async Task Grava_apaga_e_health_check_contra_o_vault() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_VAULT_IT=1 para rodar a integração Vault.");
        var provider = _fixture.Provider();
        var name = $"aedis-rw-{Guid.NewGuid():N}";

        await provider.SetSecretAsync(name, "escrito");
        (await provider.GetSecretAsync(name)).Should().Be("escrito");
        await provider.DeleteSecretAsync(name);
        (await provider.GetSecretAsync(name)).Should().BeNull();
        await provider.DeleteSecretAsync($"aedis-nunca-{Guid.NewGuid():N}");

        var health = await _fixture.HealthCheck().CheckHealthAsync(new HealthCheckContext());
        health.Status.Should().Be(HealthStatus.Healthy);
    }

    public sealed class VaultFixture : IAsyncLifetime
    {
        private IContainer? _container;

        /// <summary>Integração opt-in: liga com a env <c>AEDIS_VAULT_IT=1</c>.</summary>
        public bool Enabled { get; } = Environment.GetEnvironmentVariable("AEDIS_VAULT_IT") == "1";

        public async Task InitializeAsync() {
            if (!Enabled) return;
            _container = new ContainerBuilder()
                .WithImage("hashicorp/vault:1.18")
                .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", RootToken)
                .WithEnvironment("VAULT_DEV_LISTEN_ADDRESS", "0.0.0.0:8200")
                .WithCommand("server", "-dev")
                .WithPortBinding(8200, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPort(8200).ForPath("/v1/sys/health")))
                .Build();
            await _container.StartAsync();
        }

        public Task DisposeAsync() => _container?.DisposeAsync().AsTask() ?? Task.CompletedTask;

        public VaultSecretsProvider Provider() => VaultSecretsProvider.Create(BuildOptions());

        public VaultHealthCheck HealthCheck() =>
            new(new VaultClient(new VaultClientSettings(Address, new TokenAuthMethodInfo(RootToken))),
                NullLogger<VaultHealthCheck>.Instance);

        public async Task WriteSecretAsync(string name, string value) {
            var settings = new VaultClientSettings(Address, new TokenAuthMethodInfo(RootToken));
            var client = new VaultClient(settings);
            await client.V1.Secrets.KeyValue.V2.WriteSecretAsync(name,
                new Dictionary<string, object> { ["value"] = value }, mountPoint: "secret");
        }

        private string Address => $"http://{_container!.Hostname}:{_container.GetMappedPublicPort(8200)}";

        private VaultOptions BuildOptions() => new() { Address = Address, Token = RootToken };
    }
}
