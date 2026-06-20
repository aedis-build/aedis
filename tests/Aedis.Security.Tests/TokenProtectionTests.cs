using Aedis.Cache.Abstractions;
using Aedis.Security.Abstractions;
using Aedis.Security.Tokens;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aedis.Security.Tests;

/// <summary>
///     Garante a <see cref="CacheTokenDenylist" />: revoga por token (jti) com TTL e por usuário (corte de
///     sessão por <c>iat</c>), e consulta ambos.
/// </summary>
public sealed class CacheTokenDenylistTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Revoga_token_grava_no_cache_com_ttl() {
        var cache = Substitute.For<ICache>();
        var denylist = new CacheTokenDenylist(cache, new FixedTimeProvider(Now));
        var ttl = TimeSpan.FromMinutes(30);

        await denylist.RevokeAsync("jti-1", ttl);

        await cache.Received(1).SetStringAsync("security:token-denylist:jti-1", Arg.Any<string>(), ttl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nao_revoga_token_com_ttl_nao_positivo() {
        var cache = Substitute.For<ICache>();
        var denylist = new CacheTokenDenylist(cache, new FixedTimeProvider(Now));

        await denylist.RevokeAsync("jti-1", TimeSpan.Zero);

        await cache.DidNotReceive().SetStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consulta_revogacao_de_token_pela_existencia_da_chave() {
        var cache = Substitute.For<ICache>();
        cache.ExistsAsync("security:token-denylist:jti-1", Arg.Any<CancellationToken>()).Returns(true);
        var denylist = new CacheTokenDenylist(cache, new FixedTimeProvider(Now));

        (await denylist.IsRevokedAsync("jti-1")).Should().BeTrue();
    }

    [Fact]
    public async Task Revoga_usuario_grava_o_corte_no_instante_atual() {
        var cache = Substitute.For<ICache>();
        var denylist = new CacheTokenDenylist(cache, new FixedTimeProvider(Now));

        await denylist.RevokeUserAsync("sub-1", TimeSpan.FromHours(24));

        await cache.Received(1).SetStringAsync("security:user-revocation:sub-1",
            Now.ToUnixTimeMilliseconds().ToString(), TimeSpan.FromHours(24), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Token_emitido_antes_do_corte_de_usuario_esta_revogado() {
        var cache = Substitute.For<ICache>();
        cache.GetStringAsync("security:user-revocation:sub-1", Arg.Any<CancellationToken>())
            .Returns(Now.ToUnixTimeMilliseconds().ToString());
        var denylist = new CacheTokenDenylist(cache, new FixedTimeProvider(Now));

        (await denylist.IsUserRevokedAsync("sub-1", Now.AddHours(-1))).Should().BeTrue();
        (await denylist.IsUserRevokedAsync("sub-1", Now.AddHours(1))).Should().BeFalse();
    }
}

/// <summary>Garante o <see cref="TokenRevocation" /> administrativo: delega à denylist com a vida de revogação padrão.</summary>
public sealed class TokenRevocationTests
{
    private static IOptions<TokenDenylistOptions> Options24h() =>
        Options.Create(new TokenDenylistOptions { DefaultRevocationLifetime = TimeSpan.FromHours(24) });

    [Fact]
    public async Task Revoga_token_usa_a_vida_padrao_quando_nao_informada() {
        var denylist = Substitute.For<ITokenDenylist>();
        var revocation = new TokenRevocation(denylist, Options24h());

        await revocation.RevokeTokenAsync("jti-1");

        await denylist.Received(1).RevokeAsync("jti-1", TimeSpan.FromHours(24), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revoga_usuario_delega_com_a_vida_padrao() {
        var denylist = Substitute.For<ITokenDenylist>();
        var revocation = new TokenRevocation(denylist, Options24h());

        await revocation.RevokeUserAsync("sub-1");

        await denylist.Received(1).RevokeUserAsync("sub-1", TimeSpan.FromHours(24), Arg.Any<CancellationToken>());
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
        var abuse = new TokenAbuseGuard(user, guard, denylist, new FixedTimeProvider(Now));

        (await abuse.CheckAsync()).IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Ao_bloquear_o_token_por_abuso_revoga_o_token() {
        var (user, guard, denylist) = Substitutes();
        guard.RegisterFailureAsync("token:jti-1", Arg.Any<CancellationToken>())
            .Returns(new BruteForceStatus(true, 5, TimeSpan.FromMinutes(15)));
        guard.RegisterFailureAsync("user:sub-1", Arg.Any<CancellationToken>())
            .Returns(new BruteForceStatus(false, 1, null));
        var abuse = new TokenAbuseGuard(user, guard, denylist, new FixedTimeProvider(Now));

        var status = await abuse.RegisterFailureAsync();

        status.IsBlocked.Should().BeTrue();
        await denylist.Received(1).RevokeAsync("jti-1", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_zera_token_e_conta() {
        var (user, guard, denylist) = Substitutes();
        var abuse = new TokenAbuseGuard(user, guard, denylist, new FixedTimeProvider(Now));

        await abuse.ResetAsync();

        await guard.Received(1).ResetAsync("token:jti-1", Arg.Any<CancellationToken>());
        await guard.Received(1).ResetAsync("user:sub-1", Arg.Any<CancellationToken>());
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
