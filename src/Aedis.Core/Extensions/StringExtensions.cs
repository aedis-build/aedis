using System.Text;

namespace Aedis.Core.Extensions;

/// <summary>
///     Utilitários de string usados pela plataforma (sanitização de nomes, normalização de caminhos).
/// </summary>
public static class StringExtensions
{
    /// <summary>Trima e troca espaços por '_' (ex.: para nomes de fila/recurso).</summary>
    public static string Sanitize(this string value) {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrEmpty(value)) return value;

        var trimmed = value.Trim();
        return string.IsNullOrEmpty(trimmed) ? new string('_', value.Length) : trimmed.Replace(" ", "_");
    }

    /// <summary>Colapsa barras duplicadas para uma só, preservando o esquema http(s)://.</summary>
    public static string? RemoveDuplicateSlashes(this string? value) {
        if (string.IsNullOrEmpty(value) || value.Length < 2) return value;

        var result = new StringBuilder(value.Length);
        var charIndex = 0;

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) {
            result.Append(value.AsSpan(0, 7));
            charIndex = 7;
        }
        else if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            result.Append(value.AsSpan(0, 8));
            charIndex = 8;
        }

        var previousSlash = false;
        for (; charIndex < value.Length; charIndex++) {
            var c = value[charIndex] == '\\' ? '/' : value[charIndex];
            if (c == '/') {
                if (previousSlash) continue;
                result.Append(c);
                previousSlash = true;
            }
            else {
                result.Append(c);
                previousSlash = false;
            }
        }

        return result.ToString();
    }

    /// <summary>Normaliza barras para '/', colapsa duplicadas (preserva http(s)://).</summary>
    public static string NormalizeSlashes(this string value) {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("\\", "/").RemoveDuplicateSlashes()!;
    }

    /// <summary>Normaliza um prefixo de caminho: barras para '/', sem duplicadas, sem '/' nas pontas.</summary>
    public static string NormalizePrefix(this string? value) {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.NormalizeSlashes().Trim('/');
    }
}
