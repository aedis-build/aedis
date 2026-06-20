using Aedis.Security.Abstractions;
using Microsoft.Extensions.Options;

namespace Aedis.Security.Tokens;

/// <summary>
///     Implementação de <see cref="ITokenRevocation" /> — a API administrativa de revogação sobre a
///     <see cref="ITokenDenylist" />. Aplica a vida de revogação padrão (<see cref="TokenDenylistOptions" />)
///     quando o operador não informa a validade do token.
/// </summary>
public sealed class TokenRevocation : ITokenRevocation
{
    private readonly ITokenDenylist _denylist;
    private readonly TokenDenylistOptions _options;

    /// <summary>Cria o serviço sobre a denylist registrada e as opções vinculadas.</summary>
    public TokenRevocation(ITokenDenylist denylist, IOptions<TokenDenylistOptions> options) {
        _denylist = denylist;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task RevokeTokenAsync(string tokenId, TimeSpan? lifetime = null, CancellationToken cancellationToken = default) =>
        _denylist.RevokeAsync(tokenId, lifetime ?? _options.DefaultRevocationLifetime, cancellationToken);

    /// <inheritdoc />
    public Task RevokeUserAsync(string subject, CancellationToken cancellationToken = default) =>
        _denylist.RevokeUserAsync(subject, _options.DefaultRevocationLifetime, cancellationToken);
}
