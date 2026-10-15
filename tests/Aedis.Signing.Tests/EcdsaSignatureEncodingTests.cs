using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Aedis.Signing.Tests;

/// <summary>Conversão DER ↔ <c>r || s</c>: o que um cofre devolve vira o que a RFC 9421 exige, e volta.</summary>
public sealed class EcdsaSignatureEncodingTests
{
    [Fact]
    public void Der_do_dotnet_vira_r_s_de_64_bytes_verificavel() {
        using var key = TestSigningKeys.NewP256();
        var data = Encoding.UTF8.GetBytes("base de assinatura");

        for (var i = 0; i < 40; i++) {
            var der = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

            var raw = EcdsaSignatureEncoding.DerToIeeeP1363(der);

            raw.Should().HaveCount(64);
            key.VerifyData(data, raw, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                .Should().BeTrue($"iteração {i}");
        }
    }

    [Fact]
    public void R_s_volta_para_der_equivalente() {
        using var key = TestSigningKeys.NewP256();
        var data = Encoding.UTF8.GetBytes("x");
        var raw = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var der = EcdsaSignatureEncoding.IeeeP1363ToDer(raw);

        key.VerifyData(data, der, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence).Should().BeTrue();
        EcdsaSignatureEncoding.DerToIeeeP1363(der).Should().Equal(raw);
    }

    [Fact]
    public void Der_invalido_lanca_FormatException() {
        var act = () => EcdsaSignatureEncoding.DerToIeeeP1363(new byte[] { 0x30, 0x02, 0x02, 0x01 });
        act.Should().Throw<FormatException>();

        var act2 = () => EcdsaSignatureEncoding.IeeeP1363ToDer(new byte[63]);
        act2.Should().Throw<FormatException>();
    }

    [Fact]
    public void Inteiro_maior_que_o_campo_lanca() {
        var r = new byte[34];
        r[0] = 0x01;
        r[1] = 0xFF;
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence()) {
            writer.WriteIntegerUnsigned(r);
            writer.WriteIntegerUnsigned([0x01]);
        }

        var act = () => EcdsaSignatureEncoding.DerToIeeeP1363(writer.Encode());

        act.Should().Throw<FormatException>();
    }
}
