using System.Text;
using Aedis.Messaging.Abstractions.Serialization;
using FluentAssertions;
using Xunit;

namespace Aedis.Messaging.Tests;

/// <summary>
///     Guarda de descompressão do encoder gzip: payloads dentro do limite voltam íntegros; um payload
///     comprimido que infla além de <c>MaxDecodedBytes</c> é rejeitado antes de ocupar memória.
/// </summary>
public sealed class GzipDecodeGuardTests
{
    [Fact]
    public void Decode_devolve_o_payload_dentro_do_limite() {
        var encoder = new GzipMessageEncoder(64 * 1024);
        var payload = Encoding.UTF8.GetBytes(new string('a', 50_000));

        encoder.Decode(encoder.Encode(payload)).ToArray().Should().Equal(payload);
    }

    [Fact]
    public void Decode_lanca_acima_de_MaxDecodedBytes() {
        var bomb = new GzipMessageEncoder().Encode(new byte[2 * 1024 * 1024]);
        var encoder = new GzipMessageEncoder(100 * 1024);

        var act = () => encoder.Decode(bomb);

        act.Should().Throw<InvalidOperationException>().WithMessage("*excede o limite de 102400 bytes*");
    }

    [Fact]
    public void Limite_padrao_e_4_MiB_e_nunca_abaixo_de_1() {
        new GzipMessageEncoder().MaxDecodedBytes.Should().Be(4 * 1024 * 1024);
        new GzipMessageEncoder(0).MaxDecodedBytes.Should().Be(1);
    }
}
