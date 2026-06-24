using Aedis.Secrets.Abstractions;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Aedis.Secrets.AwsSecretsManager.Tests;

/// <summary>
///     <see cref="AwsSecretsManagerProvider" /> com o cliente AWS substituído (sem rede): mapeia
///     <c>SecretString</c>/<c>VersionId</c>/<c>CreatedDate</c> para <see cref="SecretValue" />, decodifica
///     <c>SecretBinary</c> em base64 e traduz <see cref="ResourceNotFoundException" /> em <c>null</c>.
/// </summary>
public sealed class AwsSecretsManagerProviderTests
{
    private static AwsSecretsManagerProvider Build(IAmazonSecretsManager client) =>
        new(client, NullLogger<AwsSecretsManagerProvider>.Instance);

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
}
