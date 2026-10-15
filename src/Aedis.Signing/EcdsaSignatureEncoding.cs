using System.Formats.Asn1;

namespace Aedis.Signing;

/// <summary>
///     Cofres e HSMs devolvem assinaturas ECDSA em DER (<c>SEQUENCE { INTEGER r, INTEGER s }</c>); a RFC 9421
///     (e o JOSE ES256) exigem <c>r || s</c> com tamanho fixo (IEEE P1363). Conversão nos dois sentidos, para
///     os providers que precisarem.
/// </summary>
public static class EcdsaSignatureEncoding
{
    /// <summary>Tamanho em bytes de cada coordenada para a curva P-256.</summary>
    public const int P256FieldSize = 32;

    /// <summary>DER → <c>r || s</c> com <paramref name="fieldSize" /> bytes cada (left-pad com zeros).</summary>
    public static byte[] DerToIeeeP1363(ReadOnlySpan<byte> der, int fieldSize = P256FieldSize) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fieldSize, 0);

        try {
            var reader = new AsnReader(der.ToArray(), AsnEncodingRules.DER);
            var sequence = reader.ReadSequence();
            var r = sequence.ReadIntegerBytes().Span;
            var s = sequence.ReadIntegerBytes().Span;
            sequence.ThrowIfNotEmpty();
            reader.ThrowIfNotEmpty();

            var result = new byte[fieldSize * 2];
            CopyFixed(r, result.AsSpan(0, fieldSize));
            CopyFixed(s, result.AsSpan(fieldSize, fieldSize));
            return result;
        }
        catch (AsnContentException ex) {
            throw new FormatException("A assinatura ECDSA não é um DER SEQUENCE válido de dois INTEGERs.", ex);
        }
    }

    /// <summary><c>r || s</c> → DER (para verificar com APIs que só aceitam DER).</summary>
    public static byte[] IeeeP1363ToDer(ReadOnlySpan<byte> raw) {
        if (raw.Length == 0 || raw.Length % 2 != 0)
            throw new FormatException("A assinatura IEEE P1363 precisa ter tamanho par e não nulo.");

        var half = raw.Length / 2;
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence()) {
            writer.WriteIntegerUnsigned(TrimLeadingZeros(raw[..half]));
            writer.WriteIntegerUnsigned(TrimLeadingZeros(raw[half..]));
        }

        return writer.Encode();
    }

    private static void CopyFixed(ReadOnlySpan<byte> integer, Span<byte> destination) {
        var trimmed = TrimLeadingZeros(integer);
        if (trimmed.Length > destination.Length)
            throw new FormatException($"O inteiro ECDSA tem {trimmed.Length} bytes; esperado no máximo {destination.Length}.");

        destination.Clear();
        trimmed.CopyTo(destination[(destination.Length - trimmed.Length)..]);
    }

    private static ReadOnlySpan<byte> TrimLeadingZeros(ReadOnlySpan<byte> value) {
        var i = 0;
        while (i < value.Length - 1 && value[i] == 0) i++;
        return value[i..];
    }
}
