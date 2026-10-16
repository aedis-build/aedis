namespace Aedis.Core.Extensions;

/// <summary>
///     Utilitários de string usados pela plataforma (sanitização de nomes de fila/recurso).
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
}
