using Aedis.Secrets;
using Aedis.Secrets.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Secrets.AwsSecretsManager.Tests;

/// <summary>
///     <see cref="CachingSecretsProvider" /> (sem cofre real): cacheia segredos encontrados por TTL, faz
///     single-flight sob concorrência, não cacheia leituras nulas e relê após <see cref="CachingSecretsProvider.Invalidate" />.
/// </summary>
public sealed class CachingSecretsProviderTests
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    [Fact]
    public async Task Cacheia_segredo_encontrado_dentro_do_ttl() {
        var inner = Substitute.For<ISecretsProvider>();
        inner.GetSecretWithMetadataAsync("k", Arg.Any<CancellationToken>())
            .Returns(new SecretValue("k", "v", null, null));
        var provider = new CachingSecretsProvider(inner, OneHour);

        (await provider.GetSecretAsync("k")).Should().Be("v");
        (await provider.GetSecretAsync("k")).Should().Be("v");

        await inner.Received(1).GetSecretWithMetadataAsync("k", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ttl_zero_relê_sempre() {
        var inner = Substitute.For<ISecretsProvider>();
        inner.GetSecretWithMetadataAsync("k", Arg.Any<CancellationToken>())
            .Returns(new SecretValue("k", "v", null, null));
        var provider = new CachingSecretsProvider(inner, TimeSpan.Zero);

        await provider.GetSecretAsync("k");
        await provider.GetSecretAsync("k");

        await inner.Received(2).GetSecretWithMetadataAsync("k", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Single_flight_colapsa_chamadas_concorrentes() {
        var inner = Substitute.For<ISecretsProvider>();
        inner.GetSecretWithMetadataAsync("k", Arg.Any<CancellationToken>())
            .Returns(_ => DelayedSecretAsync());
        var provider = new CachingSecretsProvider(inner, OneHour);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => provider.GetSecretAsync("k")));

        results.Should().AllBe("v");
        await inner.Received(1).GetSecretWithMetadataAsync("k", Arg.Any<CancellationToken>());
    }

    private static async Task<SecretValue?> DelayedSecretAsync() {
        await Task.Delay(50);
        return new SecretValue("k", "v", null, null);
    }

    [Fact]
    public async Task Nao_cacheia_segredo_inexistente() {
        var inner = Substitute.For<ISecretsProvider>();
        inner.GetSecretWithMetadataAsync("missing", Arg.Any<CancellationToken>())
            .Returns((SecretValue?)null);
        var provider = new CachingSecretsProvider(inner, OneHour);

        (await provider.GetSecretAsync("missing")).Should().BeNull();
        (await provider.GetSecretAsync("missing")).Should().BeNull();

        await inner.Received(2).GetSecretWithMetadataAsync("missing", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalidate_forca_releitura() {
        var inner = Substitute.For<ISecretsProvider>();
        inner.GetSecretWithMetadataAsync("k", Arg.Any<CancellationToken>())
            .Returns(new SecretValue("k", "v", null, null));
        var provider = new CachingSecretsProvider(inner, OneHour);

        await provider.GetSecretAsync("k");
        provider.Invalidate("k");
        await provider.GetSecretAsync("k");

        await inner.Received(2).GetSecretWithMetadataAsync("k", Arg.Any<CancellationToken>());
    }
}
