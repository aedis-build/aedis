using Aedis.Exceptions;
using Aedis.Tenancy.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Tenancy.Tests;

/// <summary>
///     <see cref="AmbientTenantContext" />: o escopo define o tenant do fluxo assíncrono, restaura o anterior
///     no descarte, isola fluxos concorrentes e <see cref="ICurrentTenant.RequiredId" /> falha fechado sem tenant.
/// </summary>
public sealed class AmbientTenantContextTests
{
    private readonly AmbientTenantContext _context = new();

    private ICurrentTenant Current => _context;

    [Fact]
    public void Sem_escopo_nao_ha_tenant_e_RequiredId_falha_fechado() {
        Current.Id.Should().BeNull();
        Current.HasTenant.Should().BeFalse();

        var act = () => Current.RequiredId;

        act.Should().Throw<TenantRequiredException>().Which.Should().BeAssignableTo<ForbiddenException>();
    }

    [Fact]
    public void Escopo_define_e_restaura_o_tenant_inclusive_aninhado() {
        using (_context.BeginScope("acme")) {
            Current.RequiredId.Should().Be("acme");

            using (_context.BeginScope("globex"))
                Current.Id.Should().Be("globex");

            Current.Id.Should().Be("acme");
        }

        Current.Id.Should().BeNull();
    }

    [Fact]
    public void Descartar_duas_vezes_nao_apaga_o_tenant_de_fora() {
        using var outer = _context.BeginScope("acme");
        var inner = _context.BeginScope("globex");

        inner.Dispose();
        inner.Dispose();

        Current.Id.Should().Be("acme");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejeita_tenant_vazio(string tenantId) {
        var act = () => _context.BeginScope(tenantId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Fluxos_concorrentes_nao_enxergam_o_tenant_um_do_outro() {
        async Task<string?> RunAs(string tenantId) {
            using (_context.BeginScope(tenantId)) {
                await Task.Delay(20);
                return await Task.Run(() => Current.Id);
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => RunAs($"t{i}")));

        results.Should().Equal(Enumerable.Range(0, 50).Select(i => $"t{i}"));
        Current.Id.Should().BeNull();
    }
}
