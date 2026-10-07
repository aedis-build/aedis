using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aedis.Signing.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Signing.Tests;

/// <summary>JWK/JWKS de chave P-256: campos da RFC 7517, verificação a partir do documento, thumbprint estável e rejeição de outras curvas.</summary>
public sealed class JsonWebKeySetDocumentTests
{
    [Fact]
    public void Jwk_de_chave_p256_tem_os_campos_da_rfc_7517_e_verifica_assinaturas() {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwk = JsonWebKeyDocument.FromEcPublicKey(key.ExportParameters(false), "kid-1");

        jwk.Kty.Should().Be("EC");
        jwk.Crv.Should().Be("P-256");
        jwk.X.Should().HaveLength(43, "32 bytes em base64url sem padding");
        jwk.Y.Should().HaveLength(43);
        jwk.Use.Should().Be("sig");
        jwk.Alg.Should().Be("ES256");
        jwk.KeyOps.Should().Equal("verify");

        var json = new JsonWebKeySetDocument { Keys = [jwk] }.ToJson();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("keys")[0].GetProperty("kid").GetString().Should().Be("kid-1");
        json.Should().NotContain("\"d\"", "a chave privada nunca sai");

        var data = Encoding.UTF8.GetBytes("payload");
        var signature = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        using var verifier = JsonWebKeySetDocument.Parse(json).FindByKid("kid-1")!.ToECDsa();
        verifier.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation).Should().BeTrue();
    }

    [Fact]
    public void Thumbprint_e_estavel_e_muda_com_a_chave() {
        using var a = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var b = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ja = JsonWebKeyDocument.FromEcPublicKey(a.ExportParameters(false), "x");
        var jb = JsonWebKeyDocument.FromEcPublicKey(b.ExportParameters(false), "x");

        ja.ComputeThumbprint().Should().Be(ja.ComputeThumbprint()).And.HaveLength(43);
        ja.ComputeThumbprint().Should().NotBe(jb.ComputeThumbprint());
    }

    [Fact]
    public void Jwk_que_nao_e_p256_e_rejeitado_na_conversao() {
        var act = () => new JsonWebKeyDocument { Kty = "RSA", Crv = null, Kid = "k" }.ToECDsa();
        act.Should().Throw<NotSupportedException>();
    }
}
