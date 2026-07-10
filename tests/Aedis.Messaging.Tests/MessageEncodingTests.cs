using System.Text;
using Aedis.Messaging.Abstractions.Serialization;
using FluentAssertions;
using Xunit;

namespace Aedis.Messaging.Tests;

/// <summary>
///     Codec de content-encoding: roundtrip do gzip, ganho de compressão em payload grande, política do
///     <see cref="MessageEncoderResolver" /> (limiar/ligado-desligado no publish; <c>Content-Encoding</c> no
///     consume) e a simetria do pipeline completo serialize → encode → base64 → reverso → deserialize.
/// </summary>
public sealed class MessageEncodingTests
{
    [Fact]
    public void Gzip_roundtrip_preserva_os_bytes() {
        var encoder = new GzipMessageEncoder();
        var original = Encoding.UTF8.GetBytes("conteúdo qualquer com acentuação e símbolos ~!@#");

        var restored = encoder.Decode(encoder.Encode(original)).ToArray();

        restored.Should().Equal(original);
    }

    [Fact]
    public void Gzip_comprime_payload_grande_e_compressivel() {
        var encoder = new GzipMessageEncoder();
        var big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{\"campo\":\"valor\"},", 5_000)));

        var encoded = encoder.Encode(big);

        encoded.Length.Should().BeLessThan(big.Length / 5, "JSON repetitivo comprime várias vezes");
    }

    [Fact]
    public void Identity_e_no_op() {
        var encoder = new IdentityMessageEncoder();
        var data = Encoding.UTF8.GetBytes("x");

        encoder.Encode(data).ToArray().Should().Equal(data);
        encoder.Decode(data).ToArray().Should().Equal(data);
    }

    [Theory]
    [InlineData(true, 1024, 512, "identity")]
    [InlineData(true, 1024, 2048, "gzip")]
    [InlineData(true, 1024, 1024, "gzip")]
    [InlineData(false, 1024, 4096, "identity")]
    public void Resolver_escolhe_encoder_no_publish_por_limiar_e_flag(bool enabled, int threshold, int payloadLength,
        string expected) {
        var resolver = new MessageEncoderResolver([new IdentityMessageEncoder(), new GzipMessageEncoder()],
            enabled, threshold);

        resolver.ResolveForEncode(payloadLength).Encoding.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "identity")]
    [InlineData("", "identity")]
    [InlineData("identity", "identity")]
    [InlineData("gzip", "gzip")]
    [InlineData("GZIP", "gzip")]
    public void Resolver_escolhe_encoder_no_consume_por_content_encoding(string? contentEncoding, string expected) {
        var resolver = MessageEncoderResolver.CreateDefault();

        resolver.ResolveForContentEncoding(contentEncoding).Encoding.Should().Be(expected);
    }

    [Fact]
    public void Resolver_lanca_para_encoding_desconhecido_no_consume() {
        var resolver = MessageEncoderResolver.CreateDefault();

        var act = () => resolver.ResolveForContentEncoding("brotli");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Pipeline_serializa_comprime_base64_e_reverte() {
        var serializer = new JsonMessageSerializer();
        var encoder = new GzipMessageEncoder();
        var original = new Payload("aedis", string.Concat(Enumerable.Repeat("linha de texto compressível. ", 500)));

        var serialized = serializer.Serialize(original);
        var body = Convert.ToBase64String(encoder.Encode(serialized).ToArray());

        var transported = Convert.FromBase64String(body);
        var decoded = encoder.Decode(transported);
        var restored = (Payload?)serializer.Deserialize(decoded, typeof(Payload));

        restored.Should().Be(original);
        body.Length.Should().BeLessThan(serialized.Length, "comprimido + base64 ainda é menor que o original");
    }

    private sealed record Payload(string Name, string Body);
}
