using System.Security.Claims;
using Aedis.Security.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aedis.Security.Keycloak.Tests;

/// <summary>O JwtBearer configurado por AddAedisKeycloakAuth rejeita tokens e sessões revogados via ITokenDenylist.</summary>
public sealed class KeycloakRevocationTests
{
    private static (JwtBearerOptions Options, IServiceProvider Provider) Build(ITokenDenylist? denylist) {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Auth:Authority"] = "https://auth.exemplo.com/realms/dev",
            ["Auth:Audience"] = "aedis-app"
        }).Build();
        var services = new ServiceCollection().AddLogging();
        if (denylist is not null) services.AddSingleton(denylist);
        services.AddAedisKeycloakAuth(config);
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        return (options, provider);
    }

    private static async Task<TokenValidatedContext> ValidateAsync(ITokenDenylist? denylist, params Claim[] claims) {
        var (options, provider) = Build(denylist);
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var context = new TokenValidatedContext(new DefaultHttpContext { RequestServices = provider }, scheme, options) {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"))
        };
        await options.Events.OnTokenValidated(context);
        return context;
    }

    [Fact]
    public void Configura_validacao_de_emissor_audiencia_e_claims() {
        var (options, _) = Build(null);

        options.Authority.Should().Be("https://auth.exemplo.com/realms/dev");
        options.TokenValidationParameters.ValidAudience.Should().Be("aedis-app");
        options.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        options.MapInboundClaims.Should().BeFalse();
    }

    [Fact]
    public async Task Sem_denylist_registrada_o_token_passa() {
        var context = await ValidateAsync(null, new Claim("jti", "t1"));

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task Token_revogado_por_jti_e_rejeitado() {
        var denylist = Substitute.For<ITokenDenylist>();
        denylist.IsRevokedAsync("t1", Arg.Any<CancellationToken>()).Returns(true);

        var context = await ValidateAsync(denylist, new Claim("jti", "t1"), new Claim("sub", "u1"));

        context.Result!.Failure!.Message.Should().Be("Token revogado.");
        await denylist.DidNotReceiveWithAnyArgs().IsUserRevokedAsync(default!, default, default);
    }

    [Fact]
    public async Task Sessoes_do_usuario_revogadas_apos_a_emissao_sao_rejeitadas() {
        var denylist = Substitute.For<ITokenDenylist>();
        denylist.IsUserRevokedAsync("u1", DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), Arg.Any<CancellationToken>())
            .Returns(true);

        var context = await ValidateAsync(denylist, new Claim("jti", "t2"), new Claim("sub", "u1"),
            new Claim("iat", "1700000000"));

        context.Result!.Failure!.Message.Should().Be("Sessões do usuário revogadas.");
    }

    [Fact]
    public async Task Token_sem_iat_valido_nao_consulta_revogacao_por_usuario() {
        var denylist = Substitute.For<ITokenDenylist>();

        var context = await ValidateAsync(denylist, new Claim("sub", "u1"), new Claim("iat", "nao-numerico"));

        context.Result.Should().BeNull();
        await denylist.DidNotReceiveWithAnyArgs().IsUserRevokedAsync(default!, default, default);
    }

    [Fact]
    public async Task Sem_jti_consulta_so_o_usuario_e_sem_sub_consulta_so_o_token() {
        var denylist = Substitute.For<ITokenDenylist>();

        var semJti = await ValidateAsync(denylist, new Claim("sub", "u1"), new Claim("iat", "1700000000"));
        semJti.Result.Should().BeNull();
        await denylist.DidNotReceiveWithAnyArgs().IsRevokedAsync(default!, default);
        await denylist.Received(1).IsUserRevokedAsync("u1", DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), Arg.Any<CancellationToken>());

        var semSub = await ValidateAsync(denylist, new Claim("jti", "t9"));
        semSub.Result.Should().BeNull();
        await denylist.Received(1).IsRevokedAsync("t9", Arg.Any<CancellationToken>());
        await denylist.Received(1).IsUserRevokedAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
