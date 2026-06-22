using System.Globalization;

namespace Aedis.Pdf.QuestPdf.Internal;

/// <summary>
///     Formata valores de célula de forma agnóstica e estável (datas ISO 8601, booleanos textuais,
///     <see cref="IFormattable" /> em cultura invariante) — sem assumir idioma ou fuso.
/// </summary>
internal static class PdfValueFormatter {
    public static string Format(object? value) => value switch {
        null => string.Empty,
        bool flag => flag ? "true" : "false",
        DateTime dateTime => dateTime.TimeOfDay == TimeSpan.Zero
            ? dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly timeOnly => timeOnly.ToString("HH:mm", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
