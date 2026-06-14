using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Aedis.Core.Utils;

public static partial class SnakeCaseSanitizer
{
    [GeneratedRegex(@"\.[^.]+$")]
    private static partial Regex ExtensionPattern();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumericPattern();

    [GeneratedRegex(@"_{2,}")]
    private static partial Regex MultipleUnderscoresPattern();

    public static string Sanitize(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "export";

        var withoutExtension = ExtensionPattern().Replace(fileName.Trim(), string.Empty);
        var result = ToSnakeCase(withoutExtension);
        return string.IsNullOrEmpty(result) ? "export" : result;
    }

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
