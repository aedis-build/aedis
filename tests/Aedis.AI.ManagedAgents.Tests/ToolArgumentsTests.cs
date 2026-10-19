using System.Text.Json;
using Aedis.AI.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.AI.ManagedAgents.Tests;

/// <summary>
///     Validação dos argumentos de ferramenta na fronteira de confiança: texto obrigatório ausente, vazio ou de outro
///     tipo vira <see cref="InvalidToolCallException" />; o opcional vira <c>null</c>.
/// </summary>
public class ToolArgumentsTests
{
    private static ToolCall Chamada(string input) => new("sevt_1", "passar_para", JsonDocument.Parse(input).RootElement);

    [Fact]
    public void Texto_presente_e_devolvido() {
        Chamada("""{"cargo":"sdr"}""").GetString("cargo").Should().Be("sdr");
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"cargo":"  "}""")]
    [InlineData("""{"cargo":42}""")]
    [InlineData("""["cargo"]""")]
    public void Texto_obrigatorio_invalido_lanca_com_o_nome_do_campo(string input) {
        var ler = () => Chamada(input).GetString("cargo");

        ler.Should().Throw<InvalidToolCallException>().WithMessage("*'cargo'*passar_para*");
    }

    [Fact]
    public void Texto_opcional_invalido_vira_null() {
        Chamada("""{"cargo":42}""").GetOptionalString("cargo").Should().BeNull();
    }
}
