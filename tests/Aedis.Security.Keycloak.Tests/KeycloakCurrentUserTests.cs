using System.Security.Claims;
using Aedis.Security.Abstractions;
using Aedis.Security.Keycloak;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Security.Keycloak.Tests;

/// <summary>
///     <see cref="KeycloakCurrentUser" /> lendo o usuário do token JWT (claims do Keycloak) presente no
///     <see cref="IHttpContextAccessor" />, e a registração de DI de <c>AddAedisKeycloakAuth</c>.
/// </summary>
public sealed class KeycloakCurrentUserTests
{
    private static KeycloakCurrentUser UserFrom(params Claim[] claims) {
        var identity = new ClaimsIdentity(claims, authenticationType: "jwt");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new KeycloakCurrentUser(accessor, Options.Create(new KeycloakAuthOptions()));
    }

    [Fact]
    public void Le_usuario_logado_dos_claims_keycloak() {
        var user = UserFrom(
            new Claim("sub", "user-1"),
            new Claim("name", "Joana"),
            new Claim("roles", "admin"),
            new Claim("roles", "ops"));

        user.IsAuthenticated.Should().BeTrue();
        user.Id.Should().Be("user-1");
        user.Name.Should().Be("Joana");
        user.Roles.Should().BeEquivalentTo("admin", "ops");
        user.FindClaim("sub").Should().Be("user-1");
    }

    [Fact]
    public void Anonimo_quando_nao_ha_requisicao() {
        var user = new KeycloakCurrentUser(new HttpContextAccessor(), Options.Create(new KeycloakAuthOptions()));

        user.IsAuthenticated.Should().BeFalse();
        user.Id.Should().BeNull("→ a auditoria cai no ator default");
        user.Roles.Should().BeEmpty();
    }

    [Fact]
    public void AddAedisKeycloakAuth_registra_ICurrentUser_e_options() {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Auth:Authority"] = "https://auth.exemplo.com/realms/dev",
            ["Auth:Audience"] = "aedis-app"
        }).Build();

        var services = new ServiceCollection().AddLogging();
        services.AddAedisKeycloakAuth(config);
        var provider = services.BuildServiceProvider();

        services.Should().Contain(d => d.ServiceType == typeof(ICurrentUser));
        provider.GetRequiredService<IOptions<KeycloakAuthOptions>>().Value.Authority
            .Should().Be("https://auth.exemplo.com/realms/dev");
        provider.GetService<IHttpContextAccessor>().Should().NotBeNull();
    }
}
