namespace Aedis.Security.Abstractions;

/// <summary>
///     Serviço administrativo de revogação de token, para uso por um operador/SOC (ex.: resposta a incidente,
///     logout forçado, conta comprometida). API de alto nível sobre a <see cref="ITokenDenylist" />: revoga um
///     token específico por <c>jti</c> ou todas as sessões de um usuário por <c>sub</c>, com uma vida padrão
///     de revogação configurável quando não se conhece a validade exata do token.
/// </summary>
public interface ITokenRevocation
{
    /// <summary>
    ///     Revoga o token de id <paramref name="tokenId" /> (<c>jti</c>). Quando <paramref name="lifetime" />
    ///     não é informado, usa a vida de revogação padrão configurada.
    /// </summary>
    Task RevokeTokenAsync(string tokenId, TimeSpan? lifetime = null, CancellationToken cancellationToken = default);

    /// <summary>Revoga todas as sessões/tokens atuais do usuário <paramref name="subject" /> (<c>sub</c>).</summary>
    Task RevokeUserAsync(string subject, CancellationToken cancellationToken = default);
}
