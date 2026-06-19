using System.Globalization;

namespace Aedis.Excel.Abstractions;

/// <summary>
///     Converte um valor de célula em texto de forma determinística e independente de cultura.
///     Usa formatos ISO para datas/horas e <see cref="CultureInfo.InvariantCulture" /> para qualquer
///     <see cref="IFormattable" />, evitando que a cultura do servidor altere a saída de números e datas.
/// </summary>
internal static class ColumnValueFormatter
{
    /// <summary>
    ///     Formata <paramref name="value" /> como texto: nulo vira string vazia, booleanos viram "true"/"false",
    ///     datas/horas usam formato ISO invariável e demais tipos formatáveis usam a cultura invariável.
    /// </summary>
    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
