using System.Text.Json;
using Aedis.Core.Utils;
using FluentAssertions;
using Xunit;

namespace Aedis.Core.Tests;

/// <summary>
///     Garante que o conversor de enum aceita nome em qualquer caixa e valor numérico, escreve em camelCase e,
///     para entrada inválida, informa os valores aceitos sem vazar o tipo CLR.
/// </summary>
public class FriendlyEnumConverterTests
{
    private enum Cor
    {
        Vermelho,
        VerdeClaro,
        Azul
    }

    private sealed record Payload(Cor Cor, Cor? Opcional);

    private static readonly JsonSerializerOptions Options = SystemJsonOptionsFactory.Create();

    [Theory]
    [InlineData("\"azul\"")]
    [InlineData("\"AZUL\"")]
    [InlineData("\"Azul\"")]
    public void Desserializa_nome_em_qualquer_caixa(string json) {
        var cor = JsonSerializer.Deserialize<Cor>(json, Options);

        cor.Should().Be(Cor.Azul);
    }

    [Fact]
    public void Desserializa_valor_numerico() {
        var cor = JsonSerializer.Deserialize<Cor>("1", Options);

        cor.Should().Be(Cor.VerdeClaro);
    }

    [Fact]
    public void Valor_invalido_lanca_JsonException_com_valores_aceitos() {
        var act = () => JsonSerializer.Deserialize<Cor>("\"roxo\"", Options);

        act.Should().Throw<JsonException>()
            .WithMessage("Invalid value 'roxo'. Accepted values: vermelho, verdeClaro, azul.*");
    }

    [Fact]
    public void Valor_invalido_nao_vaza_tipo_CLR() {
        var act = () => JsonSerializer.Deserialize<Payload>("{\"cor\":\"roxo\",\"opcional\":null}", Options);

        var exception = act.Should().Throw<JsonException>().Which;
        exception.Message.Should().NotContain("System.");
        exception.Message.Should().NotContain("Nullable");
        exception.Message.Should().NotContain(nameof(Cor));
    }

    [Fact]
    public void Numero_fora_do_enum_e_rejeitado_com_valores_aceitos() {
        var act = () => JsonSerializer.Deserialize<Cor>("42", Options);

        act.Should().Throw<JsonException>().WithMessage("Invalid value '42'.*");
    }

    [Fact]
    public void Serializa_usando_camelCase() {
        var json = JsonSerializer.Serialize(Cor.VerdeClaro, Options);

        json.Should().Be("\"verdeClaro\"");
    }

    [Fact]
    public void Converte_enum_anulavel_nulo_e_com_valor() {
        var comValor = JsonSerializer.Deserialize<Payload>("{\"cor\":\"azul\",\"opcional\":\"vermelho\"}", Options);
        var nulo = JsonSerializer.Deserialize<Payload>("{\"cor\":\"azul\",\"opcional\":null}", Options);

        comValor!.Opcional.Should().Be(Cor.Vermelho);
        nulo!.Opcional.Should().BeNull();
    }

    [Fact]
    public void Configure_e_idempotente_no_conversor() {
        var options = new JsonSerializerOptions();

        SystemJsonOptionsFactory.Configure(options);
        SystemJsonOptionsFactory.Configure(options);

        options.Converters.OfType<FriendlyEnumConverterFactory>().Should().ContainSingle();
    }
}
