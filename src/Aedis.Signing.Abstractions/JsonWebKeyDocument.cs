using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aedis.Signing.Abstractions;

/// <summary>Uma chave pública no formato JWK (RFC 7517). Só EC P-256 é produzido por este módulo.</summary>
public sealed record JsonWebKeyDocument
{
    /// <summary>Tipo da chave (<c>EC</c>).</summary>
    [JsonPropertyName("kty")] public string Kty { get; init; } = "EC";

    /// <summary>Curva (<c>P-256</c>).</summary>
    [JsonPropertyName("crv")] public string? Crv { get; init; } = "P-256";

    /// <summary>Coordenada X do ponto público, base64url com 32 bytes.</summary>
    [JsonPropertyName("x")] public string? X { get; init; }

    /// <summary>Coordenada Y do ponto público, base64url com 32 bytes.</summary>
    [JsonPropertyName("y")] public string? Y { get; init; }

    /// <summary>Identificador da chave, igual ao <c>keyid</c> das assinaturas.</summary>
    [JsonPropertyName("kid")] public string Kid { get; init; } = string.Empty;

    /// <summary>Uso da chave (<c>sig</c>).</summary>
    [JsonPropertyName("use")] public string? Use { get; init; } = "sig";

    /// <summary>Algoritmo JOSE equivalente (<c>ES256</c>).</summary>
    [JsonPropertyName("alg")] public string? Alg { get; init; } = "ES256";

    /// <summary>Operações permitidas (<c>verify</c>).</summary>
    [JsonPropertyName("key_ops")] public IReadOnlyList<string>? KeyOps { get; init; } = ["verify"];

    /// <summary>JWK a partir dos parâmetros públicos da curva P-256.</summary>
    public static JsonWebKeyDocument FromEcPublicKey(ECParameters parameters, string kid) {
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        if (parameters.Q.X is null || parameters.Q.Y is null)
            throw new ArgumentException("Os parâmetros EC não têm ponto público.", nameof(parameters));

        return new JsonWebKeyDocument {
            X = Base64Url.EncodeToString(PadTo32(parameters.Q.X)),
            Y = Base64Url.EncodeToString(PadTo32(parameters.Q.Y)),
            Kid = kid
        };
    }

    /// <summary>Instância <see cref="ECDsa" /> de verificação a partir do JWK (descarte após o uso).</summary>
    public ECDsa ToECDsa() {
        if (!string.Equals(Kty, "EC", StringComparison.Ordinal) || !string.Equals(Crv, "P-256", StringComparison.Ordinal))
            throw new NotSupportedException($"Só chaves EC P-256 são suportadas (kty={Kty}, crv={Crv}).");
        if (string.IsNullOrEmpty(X) || string.IsNullOrEmpty(Y))
            throw new FormatException("O JWK não tem as coordenadas x/y.");

        return ECDsa.Create(new ECParameters {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64Url.DecodeFromChars(X), Y = Base64Url.DecodeFromChars(Y) }
        });
    }

    /// <summary>Thumbprint RFC 7638 (SHA-256 sobre <c>{"crv","kty","x","y"}</c>), base64url — útil como <c>kid</c> em dev.</summary>
    public string ComputeThumbprint() {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new { crv = Crv, kty = Kty, x = X, y = Y });
        return Base64Url.EncodeToString(SHA256.HashData(canonical));
    }

    private static byte[] PadTo32(byte[] coordinate) {
        if (coordinate.Length == 32) return coordinate;
        if (coordinate.Length > 32) throw new ArgumentException("Coordenada P-256 maior que 32 bytes.");
        var padded = new byte[32];
        coordinate.CopyTo(padded, 32 - coordinate.Length);
        return padded;
    }
}

/// <summary>Documento JWKS (<c>{ "keys": [...] }</c>) publicado pela aplicação.</summary>
public sealed record JsonWebKeySetDocument
{
    /// <summary>Opções de serialização do documento (omite nulos, compacto).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>Chaves públicas publicadas.</summary>
    [JsonPropertyName("keys")] public IReadOnlyList<JsonWebKeyDocument> Keys { get; init; } = [];

    /// <summary>Serializa o documento.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Lê um documento JWKS.</summary>
    public static JsonWebKeySetDocument Parse(string json) =>
        JsonSerializer.Deserialize<JsonWebKeySetDocument>(json, JsonOptions)
        ?? throw new FormatException("Documento JWKS vazio.");

    /// <summary>Localiza a chave pelo <c>kid</c> (comparação ordinal).</summary>
    public JsonWebKeyDocument? FindByKid(string kid) =>
        Keys.FirstOrDefault(k => string.Equals(k.Kid, kid, StringComparison.Ordinal));
}
