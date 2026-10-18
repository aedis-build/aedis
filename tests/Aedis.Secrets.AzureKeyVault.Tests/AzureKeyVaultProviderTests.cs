using Aedis.Secrets.AzureKeyVault;
using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Aedis.Secrets.AzureKeyVault.Tests;

/// <summary>
///     <see cref="AzureKeyVaultProvider" /> com o <see cref="SecretClient" /> substituído (sem rede): mapeia
///     valor, versão e <c>UpdatedOn</c> para <see cref="Aedis.Secrets.Abstractions.SecretValue" /> e traduz
///     HTTP 404 em <c>null</c>.
/// </summary>
public sealed class AzureKeyVaultProviderTests
{
    private static AzureKeyVaultProvider Build(SecretClient client) =>
        new(client, NullLogger<AzureKeyVaultProvider>.Instance);

    [Fact]
    public async Task Mapeia_valor_e_metadados() {
        var updated = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var properties = SecretModelFactory.SecretProperties(version: "ver-1", updatedOn: updated);
        var secret = SecretModelFactory.KeyVaultSecret(properties, "v");

        var client = Substitute.For<SecretClient>();
        client.GetSecretAsync("k")
            .ReturnsForAnyArgs(Task.FromResult(Response.FromValue(secret, Substitute.For<Response>())));

        var result = await Build(client).GetSecretWithMetadataAsync("k");

        result.Should().NotBeNull();
        result!.Value.Should().Be("v");
        result.Version.Should().Be("ver-1");
        result.RotatedAt.Should().Be(updated);
    }

    [Fact]
    public async Task Segredo_inexistente_vira_null() {
        var client = Substitute.For<SecretClient>();
        client.GetSecretAsync("missing")
            .ThrowsAsyncForAnyArgs(new RequestFailedException(404, "não existe"));

        (await Build(client).GetSecretWithMetadataAsync("missing")).Should().BeNull();
        (await Build(client).GetSecretAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task Set_e_Delete_delegam_ao_cliente_e_remover_inexistente_e_silencioso() {
        var secret = SecretModelFactory.KeyVaultSecret(SecretModelFactory.SecretProperties(), "v");
        var client = Substitute.For<SecretClient>();
        client.SetSecretAsync("k", "v").ReturnsForAnyArgs(Task.FromResult(Response.FromValue(secret, Substitute.For<Response>())));
        client.StartDeleteSecretAsync("k").ReturnsForAnyArgs(Task.FromResult<DeleteSecretOperation>(null!));
        var provider = Build(client);

        await provider.SetSecretAsync("k", "v");
        await provider.DeleteSecretAsync("k");

        await client.Received(1).SetSecretAsync("k", "v", Arg.Any<CancellationToken>());
        await client.Received(1).StartDeleteSecretAsync("k", Arg.Any<CancellationToken>());

        var missing = Substitute.For<SecretClient>();
        missing.StartDeleteSecretAsync("x").ThrowsAsyncForAnyArgs(new RequestFailedException(404, "não existe"));
        await Build(missing).DeleteSecretAsync("x");
    }

    [Fact]
    public void Create_monta_o_provider_autonomo_a_partir_das_opcoes() =>
        AzureKeyVaultProvider.Create(new AzureKeyVaultOptions { VaultUri = "https://exemplo.vault.azure.net/" }).Should().NotBeNull();
}
