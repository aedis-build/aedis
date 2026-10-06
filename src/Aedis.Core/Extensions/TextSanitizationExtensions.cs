using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Aedis.Core.Extensions;

/// <summary>
///     Opções de saneamento de texto por classe de conteúdo. O saneamento <b>sempre</b> remove caracteres de
///     controle/formatação/não atribuídos/uso privado/substitutos isolados, separadores de linha e parágrafo e os
///     escapes textuais que decodificariam para controle; cada opção liga uma remoção adicional. Nada é
///     escapado — SQL vai por parâmetro e HTML é codificado na renderização.
/// </summary>
public sealed record TextSanitizationOptions
{
    /// <summary>Instância com todas as opções desligadas (só o saneamento sempre-ativo).</summary>
    public static TextSanitizationOptions Default { get; } = new();

    /// <summary>Preserva <c>\n</c> e <c>\r</c>, que por padrão são removidos como controle.</summary>
    public bool KeepLineBreaks { get; init; }

    /// <summary>Remove tags e entidades HTML.</summary>
    public bool RemoveHtml { get; init; }

    /// <summary>Remove aspas simples, duplas e crases.</summary>
    public bool RemoveQuotes { get; init; }

    /// <summary>Remove tokens de SQL (<c>;</c>, <c>--</c>, <c>/*</c>, <c>*/</c>).</summary>
    public bool RemoveSqlTokens { get; init; }

    /// <summary>Remove emoji e pictogramas.</summary>
    public bool RemoveEmoji { get; init; }

    /// <summary>Remove letras fora do alfabeto latino (números, pontuação e símbolos neutros ficam).</summary>
    public bool RemoveNonLatin { get; init; }

    /// <summary>Remove escapes textuais genéricos (<c>\n</c>, <c>\t</c>, <c>\uXXXX</c>, <c>\xXX</c>, <c>\\</c>).</summary>
    public bool RemoveEscapes { get; init; }
}

/// <summary>
///     Saneamento de entrada por tabela de caracteres aceitos: mantém letras, marcas, números, pontuação, símbolos
///     e espaços (categorias Unicode L, M, N, P, S e Zs), normaliza para NFC e apara as pontas. Complementa o
///     <see cref="StringExtensions.Sanitize" />, que trata nomes de fila e caminho.
/// </summary>
public static partial class TextSanitizationExtensions
{
    /// <summary>
    ///     Saneia o texto conforme as <paramref name="options" /> (ou <see cref="TextSanitizationOptions.Default" />).
    ///     <c>null</c> retorna <c>null</c>; vazio retorna vazio.
    /// </summary>
    /// <param name="value">Texto de entrada, possivelmente nulo.</param>
    /// <param name="options">Opções por classe de conteúdo; nulo usa o padrão.</param>
    /// <returns>Texto saneado, normalizado em NFC e aparado.</returns>
    public static string? SanitizeText(this string? value, TextSanitizationOptions? options = null) {
        if (value is null) return null;
        if (value.Length == 0) return value;

        options ??= TextSanitizationOptions.Default;
        var text = value;

        if (options.RemoveHtml) text = HtmlTagOrEntity().Replace(text, " ");
        text = TextualControlEscapes().Replace(text, string.Empty);
        if (options.RemoveEscapes) text = GenericTextualEscapes().Replace(text, string.Empty);
        if (options.RemoveSqlTokens) text = SqlTokens().Replace(text, " ");
        if (options.RemoveQuotes) text = Quotes().Replace(text, string.Empty);

        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            AppendIfAccepted(builder, rune, options);

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    private static void AppendIfAccepted(StringBuilder builder, Rune rune, TextSanitizationOptions options) {
        if (options.KeepLineBreaks && (rune.Value == '\n' || rune.Value == '\r')) {
            builder.Append((char)rune.Value);
            return;
        }

        var category = Rune.GetUnicodeCategory(rune);
        if (IsAlwaysRemoved(category)) return;
        if (options.RemoveEmoji && IsEmoji(rune, category)) return;
        if (options.RemoveNonLatin && IsNonLatinLetter(rune, category)) return;

        if (category == UnicodeCategory.SpaceSeparator) {
            builder.Append(' ');
            return;
        }

        builder.Append(rune.ToString());
    }

    private static bool IsAlwaysRemoved(UnicodeCategory category) => category is
        UnicodeCategory.Control or
        UnicodeCategory.Format or
        UnicodeCategory.OtherNotAssigned or
        UnicodeCategory.PrivateUse or
        UnicodeCategory.Surrogate or
        UnicodeCategory.LineSeparator or
        UnicodeCategory.ParagraphSeparator;

    /// <summary>Emoji e pictogramas: categoria <c>So</c> ou faixas de pictogramas/seletores de variação.</summary>
    private static bool IsEmoji(Rune rune, UnicodeCategory category) =>
        category == UnicodeCategory.OtherSymbol ||
        rune.Value is >= 0x1F000 and <= 0x1FAFF ||
        rune.Value is >= 0x2600 and <= 0x27BF ||
        rune.Value == 0xFE0F;

    /// <summary>Letra fora do latim (Básico, Latin-1, Estendido A/B e Adicional). Marcas, dígitos e símbolos ficam.</summary>
    private static bool IsNonLatinLetter(Rune rune, UnicodeCategory category) {
        var isLetter = category is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter;

        if (!isLetter) return false;

        return rune.Value is not (<= 0x024F or (>= 0x1E00 and <= 0x1EFF));
    }

    [GeneratedRegex(@"<[^>]*>|&[a-zA-Z#0-9]+;", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagOrEntity();

    [GeneratedRegex(@"\\u00[0-1][0-9A-Fa-f]|\\u007[Ff]|\\x[01][0-9A-Fa-f]|\\x7[Ff]|&#0*(?:[0-9]|[12][0-9]|3[01]|127);|&#[xX]0*(?:[01]?[0-9A-Fa-f]|7[Ff]);",
        RegexOptions.CultureInvariant)]
    private static partial Regex TextualControlEscapes();

    [GeneratedRegex(@"\\u[0-9A-Fa-f]{4}|\\x[0-9A-Fa-f]{2}|\\[nrtbfv0\\'""]", RegexOptions.CultureInvariant)]
    private static partial Regex GenericTextualEscapes();

    [GeneratedRegex(@"--|/\*|\*/|;", RegexOptions.CultureInvariant)]
    private static partial Regex SqlTokens();

    [GeneratedRegex(@"[""'`]", RegexOptions.CultureInvariant)]
    private static partial Regex Quotes();
}
