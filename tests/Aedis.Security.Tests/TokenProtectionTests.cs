using Aedis.Cache.Abstractions;
using Aedis.Security.Abstractions;
using Aedis.Security.Tokens;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Security.Tests;

/// <summary>
///     Garante a <see cref="CacheTokenDenylist" />: revoga um token (jti) gravando no cache com TTL e
///     consulta a revogação por existência da chave.
/// </summary>
public sealed class CacheTokenDenylistTests
{
    [Fact]
    public async Task Revoga_grava_no_cache_com_ttl() {
        var cache = Substitute.For<ICache>();
        var denylist = new CacheTokenDenylist(cache);
        var ttl = TimeSpan.FromMinutes(30);

        await denylist.RevokeAsync("jti-1", ttl);

        await cache.Received(1).SetStringAsync("security:token-denylist:jti-1", Arg.Any<string>(), ttl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nao_revoga_com_ttl_nao_positivo() {
        var cache = Substitute.For<ICache>();
        var denylist = new CacheTokenDenylist(cache);

        await denylist.RevokeAsync("jti-1", TimeSpan.Zero);

        await cache.DidNotReceive().SetStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consulta_revogacao_pela_existencia_da_chave() {
        var cache = Substitute.For<ICache>();
        cache.ExistsAsync("security:token-denylist:jti-1", Arg.Any<CancellationToken>()).Returns(true);
        var denylist = new CacheTokenDenylist(cache);

        (await denylist.IsRevokedAsync("jti-1")).Should().BeTrue();
    }
}

/// <summary>
///     Garante o <see cref="TokenAbuseGuard" />: bloqueia o token revogado, e ao bloquear o token por abuso
///     o revoga na denylist (fecha o cenário de token vazado), além de zerar token e conta no reset.
/// </summary>
public sealed class TokenAbuseGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (ICurrentUser User, IBruteForceGuard Guard, ITokenDenylist Denylist) Substitutes() {
        var user = Substitute.For<ICurrentUser>();
        user.FindClaim("jti").Returns("jti-1");
        user.Id.Returns("sub-1");
        user.FindClaim("exp").Returns(Now.AddHours(1).ToUnixTimeSeconds().ToString());
        return (user, Substitute.For<IBruteForceGuard>(), Substitute.For<ITokenDenylist>());
    }

    [Fact]
    public async Task Token_revogado_resulta_em_bloqueado() {
        var (user, guard, denylist) = Substitutes();
        denylist.IsRevokedAsync("jti-1", Arg.Any<CancellationToken>()).Returns(true);
        var abuse = new TokenAbuseGuard(user, guard, denylist, new FixedTime(Now));

        (await abuse.CheckAsync()).IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Ao_bloquear_o_token_por_abuso_revoga_o_token() {
        var (user, guard, denylist) = Substitutes();
        guard.RegisterFailureAsync("token:jti-1", Arg.Any<CancellationToken>())
            .Returns(new BruteForceStatus(true, 5, TimeSpan.FromMinutes(15)));
        guard.RegisterFailureAsync("user:sub-1", Arg.Any<CancellationToken>())
            .Returns(new BruteForceStatus(false, 1, null));
        var abuse = new TokenAbuseGuard(user, guard, denylist, new FixedTime(Now));

        var status = await abuse.RegisterFailureAsync();

        status.IsBlocked.Should().BeTrue();
        await denylist.Received(1).RevokeAsync("jti-1", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_zera_token_e_conta() {
        var (user, guard, denylist) = Substitutes();
        var abuse = new TokenAbuseGuard(user, guard, denylist, new FixedTime(Now));

        await abuse.ResetAsync();

        await guard.Received(1).ResetAsync("token:jti-1", Arg.Any<CancellationToken>());
        await guard.Received(1).ResetAsync("user:sub-1", Arg.Any<CancellationToken>());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
