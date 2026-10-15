using System.Text;
using Aedis.Messaging.Abstractions.Serialization;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Decodificação de transporte do SQS/SNS: o corpo é texto, então o payload viaja em base64
///     (<c>Content-Transfer-Encoding</c>) e, opcionalmente, comprimido (<c>Content-Encoding</c>). Sem o
///     atributo de transfer-encoding — produtor externo — o corpo é tratado como base64 quando parece base64,
///     senão como texto UTF-8 cru. A descompressão passa pelo <see cref="MessageEncoderResolver" />, cujo
///     encoder gzip limita o tamanho descomprimido.
/// </summary>
internal static class AwsMessageTransportCodec
{
    internal static byte[] Decode(string rawMessage, string? contentTransferEncoding, string? contentEncoding,
        MessageEncoderResolver encoders) {
        var transportBytes = DecodeTransferEncoding(rawMessage, contentTransferEncoding);
        return encoders.ResolveForContentEncoding(contentEncoding).Decode(transportBytes).ToArray();
    }

    private static byte[] DecodeTransferEncoding(string rawMessage, string? contentTransferEncoding) {
        if (string.IsNullOrWhiteSpace(contentTransferEncoding))
            return AwsPubSubEnvelopeParser.TryFromBase64(rawMessage) ?? Encoding.UTF8.GetBytes(rawMessage);

        if (string.Equals(contentTransferEncoding, "identity", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetBytes(rawMessage);

        if (string.Equals(contentTransferEncoding, "base64", StringComparison.OrdinalIgnoreCase))
            return Convert.FromBase64String(rawMessage);

        throw new NotSupportedException($"Content-Transfer-Encoding não suportado: '{contentTransferEncoding}'.");
    }
}
