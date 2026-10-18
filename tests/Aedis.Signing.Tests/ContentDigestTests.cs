using System.Text;
using Aedis.Signing.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Signing.Tests;

/// <summary><c>Content-Digest</c>: só <c>sha-256</c> conta, em qualquer posição do dicionário; tudo que não é um digest válido reprova.</summary>
public sealed class ContentDigestTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("corpo");

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("semigual")]
    [InlineData("=:abc:")]
    [InlineData("sha-512=:abc:")]
    [InlineData("sha-256=abc")]
    [InlineData("sha-256=:")]
    [InlineData("sha-256=:!!!!:")]
    [InlineData("sha-256=:AAAA:")]
    public void Cabecalhos_invalidos_ou_sem_sha256_reprovam(string? header) {
        ContentDigest.Verify(header, Body).Should().BeFalse();
    }

    [Fact]
    public void Sha256_e_aceito_em_qualquer_posicao_e_corpo_alterado_reprova() {
        var digest = ContentDigest.Compute(Body);

        ContentDigest.Verify(digest, Body).Should().BeTrue();
        ContentDigest.Verify($"sha-512=:AAAA:, {digest}", Body).Should().BeTrue();
        ContentDigest.Verify(digest, Encoding.UTF8.GetBytes("outro")).Should().BeFalse();
    }
}
