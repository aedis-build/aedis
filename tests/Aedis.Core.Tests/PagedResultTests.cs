using FluentAssertions;
using Xunit;

namespace Aedis.Core.Tests;

/// <summary>Testes de <see cref="PagedResult{T}" />.</summary>
public class PagedResultTests
{
    /// <summary>Map projeta os itens e preserva a paginação.</summary>
    [Fact]
    public void Map_ProjetaItens_PreservaPaginacao() {
        var page = new PagedResult<int>([1, 2], 10, 2, 2);

        var mapped = page.Map(i => i.ToString());

        mapped.Items.Should().Equal("1", "2");
        mapped.Should().BeEquivalentTo(new { Total = 10, Page = 2, PageSize = 2 });
    }
}
