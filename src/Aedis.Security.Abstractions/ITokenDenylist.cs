namespace Aedis.Security.Abstractions;

/// <summary>
///     Lista de revogação de tokens por identificador (<c>jti</c>). Como o JWT é stateless e válido até
///     expirar, esta denylist permite ao resource server <strong>revogar um token específico antes da
///     expiração</strong> — útil quando um token vaza ou é detectado em abuso. É distribuída (sobre o
///     <c>ICache</c>), valendo para toda a frota, e a entrada expira junto com o token (TTL = vida restante).
///     A imposição ocorre na validação do JWT (ver integração do provider de autenticação).
/// </summary>
public interface ITokenDenylist
{
    /// <summary>
    ///     Revoga o token de id <paramref name="tokenId" /> (<c>jti</c>) por <paramref name="ttl" /> — em geral,
    ///     a vida restante do token, para a entrada limpar-se sozinha quando ele expiraria de qualquer forma.
    /// </summary>
    Task RevokeAsync(string tokenId, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Indica se o token de id <paramref name="tokenId" /> está revogado.</summary>
    Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default);
}
