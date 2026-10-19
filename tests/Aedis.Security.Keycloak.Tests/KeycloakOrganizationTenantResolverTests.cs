using System.Security.Claims;
using Aedis.Security.Keycloak;
using Aedis.Tenancy.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Security.Keycloak.Tests;

/// <summary>
///     <see cref="KeycloakOrganizationTenantResolver" />: o tenant vem da claim <c>organization</c> do Keycloak,
///     nas formas lista de aliases e objeto por alias (com id), e falha fechado (<c>null</c>) para anônimo,
///     nenhuma organização, várias organizações ou claim malformada.
/// </summary>
public sealed class KeycloakOrganizationTenantResolverTests
{
    private static async Task<string?> ResolveAsync(bool useId, bool authenticated, params Claim[] claims) {
        var identity = new ClaimsIdentity(claims, authenticated ? "jwt" : null);
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        var resolver = new KeycloakOrganizationTenantResolver(accessor,
            Options.Create(new KeycloakAuthOptions { TenantFromOrganizationId = useId }));
        return await resolver.ResolveAsync();
    }

    private static Claim Org(string value) => new("organization", value);

    [Fact]
    public async Task Alias_da_unica_organizacao_em_lista() {
        (await ResolveAsync(false, true, Org("acme"))).Should().Be("acme");
    }

    [Fact]
    public async Task Alias_ou_id_da_unica_organizacao_em_objeto() {
        var claim = Org("""{"acme":{"id":"8f1c"}}""");

        (await ResolveAsync(false, true, claim)).Should().Be("acme");
        (await ResolveAsync(true, true, claim)).Should().Be("8f1c");
    }

    [Theory]
    [InlineData("""{"acme":{"id":"1"},"globex":{"id":"2"}}""")]
    [InlineData("""{"acme":{}}""")]
    [InlineData("{nao-e-json")]
    [InlineData("")]
    public async Task Objeto_ambiguo_sem_id_ou_malformado_nao_vira_tenant(string value) {
        (await ResolveAsync(true, true, Org(value))).Should().BeNull();
    }

    [Fact]
    public async Task Varias_organizacoes_em_lista_nao_viram_tenant() {
        (await ResolveAsync(false, true, Org("acme"), Org("globex"))).Should().BeNull();
    }

    [Fact]
    public async Task Id_exigido_e_claim_so_com_alias_nao_vira_tenant() {
        (await ResolveAsync(true, true, Org("acme"))).Should().BeNull();
    }

    [Fact]
    public async Task Anonimo_ou_sem_organizacao_nao_tem_tenant() {
        (await ResolveAsync(false, false, Org("acme"))).Should().BeNull();
        (await ResolveAsync(false, true)).Should().BeNull();
    }

    [Fact]
    public void Registro_substitui_o_resolvedor_por_claim() {
        var services = new ServiceCollection();
        services.AddScoped<ITenantResolver>(_ => null!);
        services.AddOptions<KeycloakAuthOptions>();

        services.AddAedisKeycloakOrganizationTenancy();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantResolver>().Should().BeOfType<KeycloakOrganizationTenantResolver>();
    }
}
