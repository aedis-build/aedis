using System.Security.Cryptography;

namespace Aedis.Signing;

/// <summary>
///     Header <c>Content-Digest</c> (RFC 9530) com SHA-256: <c>sha-256=:base64:</c>. É o que amarra o corpo
///     à assinatura RFC 9421 (o componente <c>content-digest</c> cobre o header, o header cobre os bytes).
/// </summary>
public static class ContentDigest
{
    /// <summary>Nome do header.</summary>
    public const string HeaderName = "Content-Digest";

    /// <summary>Chave do algoritmo no dicionário Structured Field.</summary>
    public const string Sha256Key = "sha-256";

    /// <summary>Valor do header para os bytes exatos que vão no fio.</summary>
    public static string Compute(ReadOnlySpan<byte> body) =>
        $"{Sha256Key}=:{Convert.ToBase64String(SHA256.HashData(body))}:";

    /// <summary>
    ///     Confere um <c>Content-Digest</c> recebido contra o corpo. Aceita dicionários com vários algoritmos;
    ///     exige <c>sha-256</c>. Comparação em tempo constante.
    /// </summary>
    public static bool Verify(string? headerValue, ReadOnlySpan<byte> body) {
        if (!TryParseSha256(headerValue, out var expected)) return false;
        var actual = SHA256.HashData(body);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>Extrai o digest SHA-256 de um dicionário Structured Field (<c>sha-256=:..:, sha-512=:..:</c>).</summary>
    public static bool TryParseSha256(string? headerValue, out byte[] digest) {
        digest = [];
        if (string.IsNullOrWhiteSpace(headerValue)) return false;

        foreach (var member in headerValue.Split(',')) {
            var eq = member.IndexOf('=');
            if (eq <= 0) continue;

            var key = member[..eq].Trim();
            if (!key.Equals(Sha256Key, StringComparison.OrdinalIgnoreCase)) continue;

            var value = member[(eq + 1)..].Trim();
            if (value.Length < 2 || value[0] != ':' || value[^1] != ':') return false;

            try {
                digest = Convert.FromBase64String(value[1..^1]);
                return digest.Length == 32;
            }
            catch (FormatException) {
                return false;
            }
        }

        return false;
    }
}
