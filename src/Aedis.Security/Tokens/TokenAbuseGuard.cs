using Aedis.Security.Abstractions;

namespace Aedis.Security.Tokens;

/// <summary>
///     Implementação de <see cref="ITokenAbuseGuard" /> sobre o <see cref="ICurrentUser" />: lê o <c>jti</c>
///     (token) e o <c>sub</c> (conta) do principal corrente e aplica o <see cref="IBruteForceGuard" /> a
///     ambos (chaves namespaced para não colidirem). Quando o token cruza para bloqueio, revoga-o na
///     <see cref="ITokenDenylist" /> pela vida restante (claim <c>exp</c>). Scoped — depende do usuário atual.
/// </summary>
public sealed class TokenAbuseGuard : ITokenAbuseGuard
{
    private static readonly TimeSpan FallbackRevocationTtl = TimeSpan.FromHours(1);

    private readonly ICurrentUser _currentUser;
    private readonly IBruteForceGuard _guard;
    private readonly ITokenDenylist _denylist;
    private readonly TimeProvider _timeProvider;

    /// <summary>Cria o guard combinando o usuário atual, a proteção de força bruta e a denylist de tokens.</summary>
    public TokenAbuseGuard(ICurrentUser currentUser, IBruteForceGuard guard, ITokenDenylist denylist, TimeProvider? timeProvider = null) {
        _currentUser = currentUser;
        _guard = guard;
        _denylist = denylist;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<BruteForceStatus> CheckAsync(CancellationToken cancellationToken = default) {
        var tokenId = _currentUser.FindClaim("jti");
        if (tokenId is not null && await _denylist.IsRevokedAsync(tokenId, cancellationToken))
            return new BruteForceStatus(true, 0, null);

        if (tokenId is not null) {
            var tokenStatus = await _guard.CheckAsync(TokenKey(tokenId), cancellationToken);
            if (tokenStatus.IsBlocked)
                return tokenStatus;
        }

        var subject = _currentUser.Id;
        return subject is not null ? await _guard.CheckAsync(UserKey(subject), cancellationToken) : new BruteForceStatus(false, 0, null);
    }

    /// <inheritdoc />
    public async Task<BruteForceStatus> RegisterFailureAsync(CancellationToken cancellationToken = default) {
        var tokenId = _currentUser.FindClaim("jti");
        var tokenStatus = new BruteForceStatus(false, 0, null);
        if (tokenId is not null) {
            tokenStatus = await _guard.RegisterFailureAsync(TokenKey(tokenId), cancellationToken);
            if (tokenStatus.IsBlocked)
                await RevokeCurrentTokenAsync(cancellationToken);
        }

        var subject = _currentUser.Id;
        var userStatus = subject is not null
            ? await _guard.RegisterFailureAsync(UserKey(subject), cancellationToken)
            : new BruteForceStatus(false, 0, null);

        return tokenStatus.IsBlocked ? tokenStatus : userStatus;
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default) {
        var tokenId = _currentUser.FindClaim("jti");
        if (tokenId is not null)
            await _guard.ResetAsync(TokenKey(tokenId), cancellationToken);

        var subject = _currentUser.Id;
        if (subject is not null)
            await _guard.ResetAsync(UserKey(subject), cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeCurrentTokenAsync(CancellationToken cancellationToken = default) {
        var tokenId = _currentUser.FindClaim("jti");
        if (tokenId is not null)
            await _denylist.RevokeAsync(tokenId, RemainingTokenLifetime(), cancellationToken);
    }

    private TimeSpan RemainingTokenLifetime() {
        var expiry = _currentUser.FindClaim("exp");
        if (expiry is not null && long.TryParse(expiry, out var unixSeconds)) {
            var remaining = DateTimeOffset.FromUnixTimeSeconds(unixSeconds) - _timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
                return remaining;
        }

        return FallbackRevocationTtl;
    }

    private static string TokenKey(string tokenId) => $"token:{tokenId}";
    private static string UserKey(string subject) => $"user:{subject}";
}
