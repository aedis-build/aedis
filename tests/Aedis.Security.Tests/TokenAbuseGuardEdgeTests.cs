using Aedis.Security.Abstractions;
using Aedis.Security.Tokens;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Aedis.Security.Tests;

/// <summary>Guarda anti-abuso por token e por usuário: bloqueio, revogação do token corrente, reset e registro no DI.</summary>
public sealed class TokenAbuseGuardEdgeTests
{
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IBruteForceGuard _guard = Substitute.For<IBruteForceGuard>();
    private readonly ITokenDenylist _denylist = Substitute.For<ITokenDenylist>();
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private TokenAbuseGuard Sut() {
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(Now);
        return new TokenAbuseGuard(_user, _guard, _denylist, time);
    }

    private void Logged(string? jti, string? subject, string? exp = null) {
        _user.FindClaim("jti").Returns(jti);
        _user.FindClaim("exp").Returns(exp);
        _user.Id.Returns(subject);
    }

    [Fact]
    public async Task Token_revogado_bloqueia_antes_de_consultar_o_guard() {
        Logged("t1", "u1");
        _denylist.IsRevokedAsync("t1", Arg.Any<CancellationToken>()).Returns(true);

        (await Sut().CheckAsync()).IsBlocked.Should().BeTrue();

        await _guard.DidNotReceiveWithAnyArgs().CheckAsync(default!, default);
    }

    [Fact]
    public async Task Bloqueio_por_token_prevalece_e_sem_token_consulta_so_o_usuario() {
        Logged("t1", "u1");
        _guard.CheckAsync("token:t1", Arg.Any<CancellationToken>()).Returns(new BruteForceStatus(true, 3, null));
        (await Sut().CheckAsync()).IsBlocked.Should().BeTrue();

        Logged(null, "u1");
        _guard.CheckAsync("user:u1", Arg.Any<CancellationToken>()).Returns(new BruteForceStatus(false, 1, null));
        (await Sut().CheckAsync()).FailedAttempts.Should().Be(1);

        Logged(null, null);
        (await Sut().CheckAsync()).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Falha_que_bloqueia_o_token_revoga_pelo_tempo_restante_do_token() {
        Logged("t1", "u1", exp: Now.AddMinutes(10).ToUnixTimeSeconds().ToString());
        _guard.RegisterFailureAsync("token:t1", Arg.Any<CancellationToken>()).Returns(new BruteForceStatus(true, 5, null));
        _guard.RegisterFailureAsync("user:u1", Arg.Any<CancellationToken>()).Returns(new BruteForceStatus(false, 1, null));

        var status = await Sut().RegisterFailureAsync();

        status.IsBlocked.Should().BeTrue();
        await _denylist.Received(1).RevokeAsync("t1", TimeSpan.FromMinutes(10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sem_exp_valido_a_revogacao_usa_uma_hora_e_sem_token_devolve_o_status_do_usuario() {
        Logged("t1", "u1", exp: "passado-invalido");
        await Sut().RevokeCurrentTokenAsync();
        await _denylist.Received(1).RevokeAsync("t1", TimeSpan.FromHours(1), Arg.Any<CancellationToken>());

        Logged(null, "u1");
        _guard.RegisterFailureAsync("user:u1", Arg.Any<CancellationToken>()).Returns(new BruteForceStatus(false, 2, null));
        (await Sut().RegisterFailureAsync()).FailedAttempts.Should().Be(2);
    }

    [Fact]
    public async Task Reset_limpa_token_e_usuario() {
        Logged("t1", "u1");

        await Sut().ResetAsync();

        await _guard.Received(1).ResetAsync("token:t1", Arg.Any<CancellationToken>());
        await _guard.Received(1).ResetAsync("user:u1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddAedisTokenAbuseGuard_registra_guard_denylist_revogacao_e_abuso() {
        var services = new ServiceCollection().AddAedisTokenAbuseGuard(new ConfigurationBuilder().Build());

        services.Should().Contain(d => d.ServiceType == typeof(IBruteForceGuard));
        services.Should().Contain(d => d.ServiceType == typeof(ITokenDenylist));
        services.Should().Contain(d => d.ServiceType == typeof(ITokenRevocation));
        services.Should().Contain(d => d.ServiceType == typeof(ITokenAbuseGuard) && d.Lifetime == ServiceLifetime.Scoped);
    }
}
