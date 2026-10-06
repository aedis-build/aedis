using Aedis.Secrets.Abstractions;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aedis.Secrets.AwsSecretsManager.Tests;

/// <summary>
///     <see cref="AwsSecretsManagerProvider" /> com o cliente AWS substituído (sem rede): mapeia
///     <c>SecretString</c>/<c>VersionId</c>/<c>CreatedDate</c> para <see cref="SecretValue" />, decodifica
///     <c>SecretBinary</c> em base64, traduz <see cref="ResourceNotFoundException" /> em <c>null</c> e, como
///     <see cref="ISecretsWriter" />, cria ou versiona o segredo e remove com janela de recuperação.
/// </summary>
public sealed class AwsSecretsManagerProviderTests
{
    private static AwsSecretsManagerProvider Build(IAmazonSecretsManager client, AwsSecretsManagerOptions? options = null) =>
        new(client, Options.Create(options ?? new AwsSecretsManagerOptions()), NullLogger<AwsSecretsManagerProvider>.Instance);

    [Fact]
    public async Task Mapeia_valor_e_metadados() {
        const string versionId = "00000000-0000-0000-0000-000000000001";
        var created = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var client = Substitute.For<IAmazonSecretsManager>();
        client.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretString = "v", VersionId = versionId, CreatedDate = created });

        var secret = await Build(client).GetSecretWithMetadataAsync("k");

        secret.Should().NotBeNull();
        secret!.Name.Should().Be("k");
        secret.Value.Should().Be("v");
        secret.Version.Should().Be(versionId);
        secret.RotatedAt.Should().Be(new DateTimeOffset(created));
    }

    [Fact]
    public async Task Decodifica_secret_binario_em_base64() {
        var bytes = "binário"u8.ToArray();
        var client = Substitute.For<IAmazonSecretsManager>();
        client.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretBinary = new MemoryStream(bytes) });

        var value = await Build(client).GetSecretAsync("k");

        value.Should().Be(Convert.ToBase64String(bytes));
    }

    [Fact]
    public async Task Segredo_inexistente_vira_null() {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns<GetSecretValueResponse>(_ => throw new ResourceNotFoundException("não existe"));

        (await Build(client).GetSecretWithMetadataAsync("missing")).Should().BeNull();
        (await Build(client).GetSecretAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task Set_cria_o_segredo_com_a_chave_kms_das_opcoes() {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.CreateSecretAsync(Arg.Any<CreateSecretRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateSecretResponse());

        await Build(client, new AwsSecretsManagerOptions { KmsKeyId = "alias/app" }).SetSecretAsync("k", "v");

        await client.Received(1).CreateSecretAsync(
            Arg.Is<CreateSecretRequest>(r => r.Name == "k" && r.SecretString == "v" && r.KmsKeyId == "alias/app"),
            Arg.Any<CancellationToken>());
        await client.DidNotReceive().PutSecretValueAsync(Arg.Any<PutSecretValueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_grava_nova_versao_quando_o_segredo_ja_existe() {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.CreateSecretAsync(Arg.Any<CreateSecretRequest>(), Arg.Any<CancellationToken>())
            .Returns<CreateSecretResponse>(_ => throw new ResourceExistsException("já existe"));
        client.PutSecretValueAsync(Arg.Any<PutSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutSecretValueResponse());

        await Build(client).SetSecretAsync("k", "v2");

        await client.Received(1).PutSecretValueAsync(
            Arg.Is<PutSecretValueRequest>(r => r.SecretId == "k" && r.SecretString == "v2"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_usa_a_janela_de_recuperacao_e_ignora_inexistente() {
        var client = Substitute.For<IAmazonSecretsManager>();
        var calls = 0;
        client.DeleteSecretAsync(Arg.Any<DeleteSecretRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? Task.FromResult(new DeleteSecretResponse())
                : Task.FromException<DeleteSecretResponse>(new ResourceNotFoundException("não existe")));
        var provider = Build(client, new AwsSecretsManagerOptions { DeletionRecoveryWindowDays = 7 });

        await provider.DeleteSecretAsync("k");
        var second = async () => await provider.DeleteSecretAsync("k");

        await client.Received().DeleteSecretAsync(
            Arg.Is<DeleteSecretRequest>(r => r.SecretId == "k" && r.RecoveryWindowInDays == 7), Arg.Any<CancellationToken>());
        await second.Should().NotThrowAsync();
    }
}
