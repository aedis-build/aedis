using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Aedis.Core.Utils;

/// <summary>
///     Converte nomes (de arquivo ou texto livre) em identificadores <c>snake_case</c> seguros para uso
///     como nomes de recurso, chave ou caminho. Remove acentos, baixa a caixa e troca tudo que não for
///     alfanumérico por <c>_</c>, colapsando repetições. Usa regex compilada por gerador de código.
/// </summary>
public static partial class SnakeCaseSanitizer
{
    [GeneratedRegex(@"\.[^.]+$")]
    private static partial Regex ExtensionPattern();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumericPattern();

    [GeneratedRegex(@"_{2,}")]
    private static partial Regex MultipleUnderscoresPattern();

    /// <summary>
    ///     Sanitiza um nome de arquivo: remove a extensão e converte o restante para <c>snake_case</c>.
    ///     Retorna <c>"export"</c> quando a entrada é vazia/em branco ou nada sobra após a sanitização.
    /// </summary>
    /// <param name="fileName">Nome de arquivo a sanitizar (com ou sem extensão).</param>
    /// <returns>Nome em <c>snake_case</c> sem extensão, ou <c>"export"</c> como fallback.</returns>
    public static string Sanitize(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "export";

        var withoutExtension = ExtensionPattern().Replace(fileName.Trim(), string.Empty);
        var result = ToSnakeCase(withoutExtension);
        return string.IsNullOrEmpty(result) ? "export" : result;
    }

    /// <summary>
    ///     Converte um texto qualquer para <c>snake_case</c>: normaliza (remove acentos via decomposição
    ///     Unicode), baixa a caixa, substitui sequências não alfanuméricas por <c>_</c> e remove
    ///     <c>_</c> nas pontas. Retorna string vazia para entrada vazia/em branco.
    /// </summary>
    /// <param name="value">Texto a converter.</param>
    /// <returns>Texto em <c>snake_case</c>, ou string vazia se a entrada for vazia/em branco.</returns>
    public static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);

        var asciiOnly = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                asciiOnly.Append(c);
        }

        var lower = asciiOnly.ToString().ToLowerInvariant();
        var snakeCase = NonAlphanumericPattern().Replace(lower, "_");
        return MultipleUnderscoresPattern().Replace(snakeCase, "_").Trim('_');
    }
}
