using System.Security.Cryptography;

namespace Aedis.Signing.Abstractions;

/// <summary>
///     Chave de assinatura resolvida no bootstrap: identidade no provider + chave pública em memória. A chave
///     privada nunca está aqui (num HSM ela não sai do cofre; em processo fica no provider).
/// </summary>
/// <param name="KeyId">Identificador publicado como <c>keyid</c>/<c>kid</c> (nunca um ARN ou URL interna).</param>
/// <param name="ProviderKeyReference">Referência usada nas chamadas ao provider (ARN, URL da versão…). <c>null</c> em processo.</param>
/// <param name="Algorithm">Algoritmo RFC 9421 (<c>ecdsa-p256-sha256</c>).</param>
/// <param name="PublicKey">Parâmetros públicos da curva P-256 (Q.X, Q.Y).</param>
/// <param name="Jwk">A mesma chave pública como JWK (o que sai no JWKS).</param>
public sealed record SigningKeyHandle(
    string KeyId,
    string? ProviderKeyReference,
    string Algorithm,
    ECParameters PublicKey,
    JsonWebKeyDocument Jwk)
{
    /// <summary>Referência para o provider (a específica quando há; senão o <see cref="KeyId" />).</summary>
    public string ProviderReference => ProviderKeyReference ?? KeyId;

    /// <summary>Instância <see cref="ECDsa" /> só com a chave pública (verificação). Descarte após o uso.</summary>
    public ECDsa CreatePublicKey() => ECDsa.Create(PublicKey);

    /// <inheritdoc />
    public override string ToString() => $"SigningKeyHandle {{ KeyId = {KeyId}, Algorithm = {Algorithm} }}";
}
