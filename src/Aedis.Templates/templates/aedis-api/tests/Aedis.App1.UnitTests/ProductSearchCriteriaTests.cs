using Aedis.App1.Infrastructure.Queries;
using FluentAssertions;
using Xunit;

namespace Aedis.App1.UnitTests;

/// <summary>
///     Testa o SQL gerado pelo critério de busca: paginação quando há página, e ausência de <c>LIMIT</c>/
///     <c>ORDER BY</c> na variante de contagem (que será envolvida em <c>SELECT count(*)</c>).
/// </summary>
public sealed class ProductSearchCriteriaTests {
    [Fact]
    public void Aplica_filtros_e_paginacao_quando_ha_pagina() {
        var (sql, _) = new ProductSearchCriteria("ABC", "wid", page: 2, pageSize: 10).Build();

        sql.Should().Contain("FROM product");
        sql.Should().Contain("is_deleted");
        sql.Should().Contain("code");
        sql.Should().Contain("name");
        sql.Should().Contain("LIMIT 10");
        sql.Should().Contain("OFFSET 10");
    }

    [Fact]
    public void Variante_de_contagem_nao_pagina() {
        var (sql, _) = new ProductSearchCriteria("ABC", null).Build();

        sql.Should().NotContain("LIMIT");
        sql.Should().NotContain("ORDER BY");
    }
}
