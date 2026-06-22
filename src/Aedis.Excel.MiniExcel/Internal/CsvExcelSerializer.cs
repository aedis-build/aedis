using System.Text;
using Aedis.Excel.Abstractions;

namespace Aedis.Excel.MiniExcel.Internal;

/// <summary>
///     Serializa linhas em CSV com controle de delimitador, BOM UTF-8 e escaping RFC 4180. Usado quando o
///     formato pedido é <see cref="ExcelFormat.Csv" /> — o XLSX é delegado ao MiniExcel.
/// </summary>
internal static class CsvExcelSerializer {
    public static async Task WriteAsync(Stream stream, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, CsvDelimiter delimiter, CancellationToken cancellationToken) {
        var separator = ToChar(delimiter);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

        await writer.WriteLineAsync(string.Join(separator, headers.Select(header => Escape(header, separator))));

        foreach (var row in rows) {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(separator, row.Select(value => Escape(ColumnValueFormatter.Format(value), separator))));
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static string Escape(string value, char separator) {
        if (value.IndexOf(separator) >= 0 || value.Contains('"') || value.Contains('\n') || value.Contains('\r')) {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static char ToChar(CsvDelimiter delimiter) => delimiter switch {
        CsvDelimiter.Semicolon => ';',
        CsvDelimiter.Tab => '\t',
        _ => ','
    };
}
