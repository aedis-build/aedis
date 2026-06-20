namespace Aedis.Security.Abstractions;

/// <summary>
///     Lista de revogação de tokens. Como o JWT é stateless e válido até expirar, esta denylist permite ao
///     resource server <strong>revogar tokens antes da expiração</strong> — por token individual (<c>jti</c>,
///     ex.: token vazado) ou para todas as sessões de um usuário de uma vez (corte por instante de emissão
///     <c>iat</c>, ex.: conta comprometida). É distribuída (sobre o <c>ICache</c>), valendo para toda a frota,
///     e a imposição ocorre na validação do JWT (o provider consulta esta denylist e recusa o token).
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

    /// <summary>
    ///     Revoga <strong>todos os tokens já emitidos</strong> para o usuário <paramref name="subject" />
    ///     (<c>sub</c>), gravando um corte no instante atual: tokens emitidos antes dele passam a ser
    ///     recusados. <paramref name="ttl" /> deve cobrir a maior vida possível de um token em circulação.
    /// </summary>
    Task RevokeUserAsync(string subject, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Indica se um token do usuário <paramref name="subject" />, emitido em
    ///     <paramref name="tokenIssuedAt" /> (<c>iat</c>), está revogado por um corte de sessão anterior.
    /// </summary>
    Task<bool> IsUserRevokedAsync(string subject, DateTimeOffset tokenIssuedAt, CancellationToken cancellationToken = default);
}
