using Aedis.Core.Utils;
using FluentAssertions;
using Xunit;

namespace Aedis.Core.Tests;

/// <summary>Testes de <see cref="SnakeCaseSanitizer" /> (nomes de arquivo de exportação).</summary>
public class SnakeCaseSanitizerTests
{
    /// <summary>Remove extensão e acentos, converte para snake_case e colapsa separadores.</summary>
    [Theory]
    [InlineData("Relatório de Vendas.xlsx", "relatorio_de_vendas")]
    [InlineData("  Ordens--Pagas__2026 .pdf", "ordens_pagas_2026")]
    [InlineData("", "export")]
    [InlineData("   ", "export")]
    [InlineData("!!!.csv", "export")]
    public void Sanitize_GeraSnakeCaseSemExtensao(string input, string expected) {
        SnakeCaseSanitizer.Sanitize(input).Should().Be(expected);
    }

    /// <summary>ToSnakeCase devolve vazio para entrada em branco.</summary>
    [Fact]
    public void ToSnakeCase_Branco_DevolveVazio() {
        SnakeCaseSanitizer.ToSnakeCase(" ").Should().BeEmpty();
    }
}
