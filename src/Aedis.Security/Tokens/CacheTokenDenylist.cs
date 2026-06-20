using Aedis.Cache.Abstractions;
using Aedis.Security.Abstractions;

namespace Aedis.Security.Tokens;

/// <summary>
///     Implementação de <see cref="ITokenDenylist" /> sobre o <see cref="ICache" /> do Aedis: a revogação é
///     distribuída (vale para toda a frota) e a entrada expira junto com o token. A imposição ocorre na
///     validação do JWT (o provider de autenticação consulta <see cref="IsRevokedAsync" /> e recusa o token).
/// </summary>
public sealed class CacheTokenDenylist : ITokenDenylist
{
    private const string KeyPrefix = "security:token-denylist:";

    private readonly ICache _cache;

    /// <summary>Cria a denylist sobre o <see cref="ICache" /> registrado pela aplicação.</summary>
    public CacheTokenDenylist(ICache cache) => _cache = cache;

    /// <inheritdoc />
    public Task RevokeAsync(string tokenId, TimeSpan ttl, CancellationToken cancellationToken = default) {
        if (string.IsNullOrEmpty(tokenId) || ttl <= TimeSpan.Zero)
            return Task.CompletedTask;

        return _cache.SetStringAsync(Key(tokenId), "revoked", ttl, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default) =>
        string.IsNullOrEmpty(tokenId) ? Task.FromResult(false) : _cache.ExistsAsync(Key(tokenId), cancellationToken);

    private static string Key(string tokenId) => $"{KeyPrefix}{tokenId}";
}
