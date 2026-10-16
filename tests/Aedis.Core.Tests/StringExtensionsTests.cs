using Aedis.Core.Extensions;
using FluentAssertions;
using Xunit;

namespace Aedis.Core.Tests;

/// <summary>Testes de <see cref="StringExtensions.Sanitize" />.</summary>
public class StringExtensionsTests
{
    /// <summary>Trima e troca espaços internos por '_'; só espaços vira '_' no mesmo tamanho.</summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("  orders queue  ", "orders_queue")]
    [InlineData("orders", "orders")]
    [InlineData("   ", "___")]
    public void Sanitize_NormalizaEspacos(string input, string expected) {
        input.Sanitize().Should().Be(expected);
    }

    /// <summary>Nulo é rejeitado.</summary>
    [Fact]
    public void Sanitize_Nulo_Lanca() {
        var act = () => ((string)null!).Sanitize();

        act.Should().Throw<ArgumentNullException>();
    }
}
