using Aedis.Domain.Entities;
using Aedis.Domain.Specifications;
using FluentAssertions;
using Xunit;

namespace Aedis.Domain.Tests;

/// <summary>
///     Entidades comparam por identidade (mesmo tipo e mesmo Id; transientes nunca são iguais) e o agregado
///     auditável expõe as colunas de auditoria; especificações compõem com And/Or.
/// </summary>
public sealed class EntitiesAndSpecificationsTests
{
    private sealed class Pedido : AggregateRoot<Guid>;

    private sealed class Cliente : AggregateRoot<Guid>;

    private sealed class Conta : AuditableAggregateRoot<int>;

    private sealed class Par : SpecificationBase<int>
    {
        public override bool IsSatisfiedBy(int candidate) => candidate % 2 == 0;
    }

    private sealed class Positivo : SpecificationBase<int>
    {
        public override bool IsSatisfiedBy(int candidate) => candidate > 0;
    }

    [Fact]
    public void Entidades_com_o_mesmo_id_e_tipo_sao_iguais() {
        var id = Guid.NewGuid();
        var a = new Pedido { Id = id };
        var b = new Pedido { Id = id };

        a.Equals(b).Should().BeTrue();
        a.Equals((object)b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Equals(a).Should().BeTrue();
    }

    [Fact]
    public void Tipos_diferentes_nulo_e_transientes_nao_sao_iguais() {
        var id = Guid.NewGuid();

        new Pedido { Id = id }.Equals(new Cliente { Id = id }).Should().BeFalse();
        new Pedido { Id = id }.Equals(null).Should().BeFalse();
        new Pedido().Equals(new Pedido()).Should().BeFalse("transientes não têm identidade");
        new Pedido().GetHashCode().Should().NotBe(0);
        (new Pedido() is IAggregateRoot).Should().BeTrue();
    }

    [Fact]
    public void Agregado_auditavel_carrega_as_colunas_de_auditoria() {
        var now = DateTimeOffset.UtcNow;
        var conta = new Conta { Id = 1, CreatedAt = now, CreatedBy = "u1", UpdatedAt = now, UpdatedBy = "u2", UpdatedReason = "ajuste", IsDeleted = true, DeletedAt = now, DeletedBy = "u3" };

        conta.Should().BeAssignableTo<IAuditable>();
        conta.CreatedBy.Should().Be("u1");
        conta.UpdatedReason.Should().Be("ajuste");
        conta.DeletedBy.Should().Be("u3");
        conta.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void Especificacoes_compoem_com_And_e_Or() {
        var parPositivo = new Par().And(new Positivo());
        var parOuPositivo = new Par().Or(new Positivo());

        parPositivo.IsSatisfiedBy(4).Should().BeTrue();
        parPositivo.IsSatisfiedBy(-4).Should().BeFalse();
        parOuPositivo.IsSatisfiedBy(-4).Should().BeTrue();
        parOuPositivo.IsSatisfiedBy(-3).Should().BeFalse();
        parPositivo.And(parOuPositivo).IsSatisfiedBy(2).Should().BeTrue();
    }
}
