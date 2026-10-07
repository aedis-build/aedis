using Aedis.Core.Extensions;
using FluentAssertions;
using Xunit;

namespace Aedis.Core.Tests;

/// <summary>
///     Garante o saneamento sempre-ativo (controle, formatação, separadores e escapes textuais de controle) e
///     cada remoção opcional por classe de conteúdo, preservando letras acentuadas, números e pontuação.
///     Caracteres especiais são construídos por código para o teste não depender de escapes no fonte.
/// </summary>
public class TextSanitizationExtensionsTests
{
    private const char Nul = (char)0x0000;
    private const char Esc = (char)0x001B;
    private const char ZeroWidthSpace = (char)0x200B;
    private const char LineSeparator = (char)0x2028;
    private const char NoBreakSpace = (char)0x00A0;
    private const char EmSpace = (char)0x2003;
    private const char CombiningAcute = (char)0x0301;
    private const char EAcute = (char)0x00E9;
    private const char Backslash = (char)92;
    private static readonly string Emoji = char.ConvertFromUtf32(0x1F600);
    private static readonly string CheckMark = char.ConvertFromUtf32(0x2705);

    [Fact]
    public void Entrada_nula_retorna_nula() {
        ((string?)null).SanitizeText().Should().BeNull();
    }

    [Fact]
    public void Remove_controles_formatacao_e_separadores_unicode() {
        var entrada = "ab" + Nul + "c" + Esc + "d" + ZeroWidthSpace + "e" + LineSeparator + "f";

        entrada.SanitizeText().Should().Be("abcdef");
    }

    [Fact]
    public void Preserva_letras_numeros_pontuacao_e_acentos() {
        var entrada = "João, 42 anos — Ação: R$ 10,50!";

        entrada.SanitizeText().Should().Be(entrada);
    }

    [Fact]
    public void Normaliza_para_NFC() {
        var decomposto = "e" + CombiningAcute;

        decomposto.SanitizeText().Should().Be(EAcute.ToString());
    }

    [Fact]
    public void Remove_escape_textual_de_controle_mas_mantem_escape_comum() {
        var escapeNul = Backslash + "u0000";
        var escapeEsc = Backslash + "x1B";
        var escapeNovaLinha = Backslash + "n";
        var entrada = "ok" + escapeNul + "ok" + escapeEsc + "ok&#0;ok" + escapeNovaLinha + "fim";

        entrada.SanitizeText().Should().Be("okokokok" + escapeNovaLinha + "fim");
    }

    [Fact]
    public void RemoveEscapes_remove_escapes_genericos() {
        var entrada = "linha" + Backslash + "nquebra" + Backslash + "ttab" + Backslash + "u00E9";

        entrada.SanitizeText(new TextSanitizationOptions { RemoveEscapes = true })
            .Should().Be("linhaquebratab");
    }

    [Fact]
    public void Quebras_de_linha_sao_removidas_por_padrao_e_preservadas_com_KeepLineBreaks() {
        var entrada = "a" + (char)10 + "b";

        entrada.SanitizeText().Should().Be("ab");
        entrada.SanitizeText(new TextSanitizationOptions { KeepLineBreaks = true }).Should().Be(entrada);
    }

    [Fact]
    public void RemoveHtml_remove_tags_e_entidades() {
        var entrada = "<b>oi</b>&nbsp;tudo&amp;bem";

        entrada.SanitizeText(new TextSanitizationOptions { RemoveHtml = true })
            .Should().Be("oi  tudo bem", "cada tag ou entidade removida vira um espaço e o texto não é recolapsado");
    }

    [Fact]
    public void RemoveSqlTokens_remove_tokens() {
        var entrada = "x; drop--/*c*/y";

        entrada.SanitizeText(new TextSanitizationOptions { RemoveSqlTokens = true })
            .Should().Be("x  drop  c y", "cada token removido vira um espaço e o texto não é recolapsado");
    }

    [Fact]
    public void RemoveQuotes_remove_aspas_e_crases() {
        var entrada = "a" + (char)34 + "b" + (char)39 + "c" + (char)96 + "d";

        entrada.SanitizeText(new TextSanitizationOptions { RemoveQuotes = true })
            .Should().Be("abcd");
    }

    [Fact]
    public void RemoveEmoji_remove_emoji() {
        var entrada = "oi " + Emoji + " tudo " + CheckMark + " bem";

        entrada.SanitizeText(new TextSanitizationOptions { RemoveEmoji = true })
            .Should().Be("oi  tudo  bem");
    }

    [Fact]
    public void RemoveNonLatin_remove_letras_fora_do_latim_mantendo_acentos_e_numeros() {
        var ideogramas = char.ConvertFromUtf32(0x6771) + char.ConvertFromUtf32(0x4EAC);
        var entrada = "café " + ideogramas + " 42 ünïcode";

        entrada.SanitizeText(new TextSanitizationOptions { RemoveNonLatin = true })
            .Should().Be("café  42 ünïcode");
    }

    [Fact]
    public void Espacos_unicode_viram_espaco_comum_e_pontas_sao_aparadas() {
        var entrada = NoBreakSpace + " a" + EmSpace + "b " + NoBreakSpace;

        entrada.SanitizeText().Should().Be("a b");
    }
}
