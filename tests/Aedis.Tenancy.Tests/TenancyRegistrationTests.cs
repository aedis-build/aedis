using Aedis.Security.Abstractions;
using Aedis.Tenancy.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aedis.Tenancy.Tests;

/// <summary>
///     <c>AddAedisTenancy</c>: um único tenant ambiente atrás de <see cref="ITenantContext" /> e
///     <see cref="ICurrentTenant" />, o <see cref="ClaimTenantResolver" /> lendo a claim configurada, a
///     validação das opções e a precedência de um resolvedor registrado pela aplicação.
/// </summary>
public sealed class TenancyRegistrationTests
{
    private static ServiceProvider Build(Action<IServiceCollection>? extra = null,
        Dictionary<string, string?>? settings = null) {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        var services = new ServiceCollection();
        extra?.Invoke(services);
        services.AddAedisTenancy(configuration);
        return services.BuildServiceProvider();
    }

    private static ICurrentUser User(bool authenticated, string claimType, string? value) {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(authenticated);
        user.FindClaim(claimType).Returns(value);
        return user;
    }

    [Fact]
    public void Contexto_e_tenant_atual_sao_a_mesma_instancia_singleton() {
        using var provider = Build();
        var context = provider.GetRequiredService<ITenantContext>();

        using (context.BeginScope("acme"))
            provider.GetRequiredService<ICurrentTenant>().Id.Should().Be("acme");

        provider.GetRequiredService<ICurrentTenant>().Should().BeSameAs(context);
    }

    [Fact]
    public async Task Resolve_o_tenant_da_claim_configurada() {
        using var provider = Build(s => s.AddScoped(_ => User(true, "empresa", "acme")),
            new Dictionary<string, string?> { ["Tenancy:ClaimType"] = "empresa" });
        using var scope = provider.CreateScope();

        var tenant = await scope.ServiceProvider.GetRequiredService<ITenantResolver>().ResolveAsync();

        tenant.Should().Be("acme");
    }

    [Theory]
    [InlineData(false, "acme")]
    [InlineData(true, null)]
    [InlineData(true, " ")]
    public async Task Sem_tenant_para_anonimo_ou_claim_vazia(bool authenticated, string? claim) {
        var resolver = new ClaimTenantResolver(Options.Create(new TenancyOptions()),
            User(authenticated, "tenant_id", claim));

        (await resolver.ResolveAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Sem_usuario_registrado_como_em_worker_nao_ha_tenant() {
        using var provider = Build();
        using var scope = provider.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<ITenantResolver>().ResolveAsync()).Should().BeNull();
    }

    [Fact]
    public void Resolvedor_da_aplicacao_tem_precedencia() {
        var custom = Substitute.For<ITenantResolver>();
        using var provider = Build(s => s.AddScoped(_ => custom));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantResolver>().Should().BeSameAs(custom);
    }

    [Fact]
    public void Claim_vazia_na_configuracao_falha_na_validacao() {
        using var provider = Build(settings: new Dictionary<string, string?> { ["Tenancy:ClaimType"] = "" });

        var act = () => provider.GetRequiredService<IOptions<TenancyOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }
}
