using Aedis.Cache.Abstractions;
using Aedis.Security.Abstractions;

namespace Aedis.Security.Tokens;

/// <summary>
///     Implementação de <see cref="ITokenDenylist" /> sobre o <see cref="ICache" /> do Aedis. Revogação por
///     token (<c>jti</c>) grava um marcador com TTL; revogação por usuário grava um corte no instante atual,
///     e tokens emitidos antes dele são recusados. Distribuída (vale para a frota); a imposição ocorre na
///     validação do JWT.
/// </summary>
public sealed class CacheTokenDenylist : ITokenDenylist
{
    private const string TokenKeyPrefix = "security:token-denylist:";
    private const string UserKeyPrefix = "security:user-revocation:";

    private readonly ICache _cache;
    private readonly TimeProvider _timeProvider;

    /// <summary>Cria a denylist sobre o <see cref="ICache" /> registrado, usando o <see cref="TimeProvider" /> informado (ou o do sistema).</summary>
    public CacheTokenDenylist(ICache cache, TimeProvider? timeProvider = null) {
        _cache = cache;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task RevokeAsync(string tokenId, TimeSpan ttl, CancellationToken cancellationToken = default) {
        if (string.IsNullOrEmpty(tokenId) || ttl <= TimeSpan.Zero)
            return Task.CompletedTask;

        return _cache.SetStringAsync(TokenKey(tokenId), "revoked", ttl, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default) =>
        string.IsNullOrEmpty(tokenId) ? Task.FromResult(false) : _cache.ExistsAsync(TokenKey(tokenId), cancellationToken);

    /// <inheritdoc />
    public Task RevokeUserAsync(string subject, TimeSpan ttl, CancellationToken cancellationToken = default) {
        if (string.IsNullOrEmpty(subject) || ttl <= TimeSpan.Zero)
            return Task.CompletedTask;

        var cutoff = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString();
        return _cache.SetStringAsync(UserKey(subject), cutoff, ttl, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsUserRevokedAsync(string subject, DateTimeOffset tokenIssuedAt, CancellationToken cancellationToken = default) {
        if (string.IsNullOrEmpty(subject))
            return false;

        var value = await _cache.GetStringAsync(UserKey(subject), cancellationToken);
        return value is not null
               && long.TryParse(value, out var cutoffMs)
               && tokenIssuedAt < DateTimeOffset.FromUnixTimeMilliseconds(cutoffMs);
    }

    private static string TokenKey(string tokenId) => $"{TokenKeyPrefix}{tokenId}";
    private static string UserKey(string subject) => $"{UserKeyPrefix}{subject}";
}
