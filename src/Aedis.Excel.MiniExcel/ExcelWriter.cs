using Aedis.Core.Utils;
using Aedis.Excel.Abstractions;
using Aedis.Excel.MiniExcel.Internal;
using Microsoft.Extensions.Options;
using MiniExcelLibs;

namespace Aedis.Excel.MiniExcel;

/// <summary>
///     Implementação de <see cref="IExcelWriter" /> sobre a biblioteca MiniExcel. Projeta as linhas a partir
///     das colunas declaradas, escolhe o backing do stream pelo volume estimado (memória até o limiar,
///     arquivo temporário com <c>DeleteOnClose</c> acima dele, para não estourar a heap em exportações grandes)
///     e produz XLSX (via MiniExcel) ou CSV (com delimitador/escaping próprios). O nome do arquivo é sanitizado.
/// </summary>
public sealed class ExcelWriter : IExcelWriter {
    private const int FileStreamBufferSize = 1024 * 1024;

    private readonly ExcelWriterOptions _options;

    /// <summary>
    ///     Cria o writer com as opções de exportação.
    /// </summary>
    /// <param name="options">Opções (limiar de linhas para arquivo temporário, nome da aba).</param>
    public ExcelWriter(IOptions<ExcelWriterOptions> options) {
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ExcelResult> WriteAsync<T>(
        IEnumerable<T> rows,
        IReadOnlyList<ExcelColumn<T>> columns,
        string fileName,
        ExcelFormat format = ExcelFormat.Xlsx,
        CsvDelimiter delimiter = CsvDelimiter.Comma,
        int estimatedRowCount = 0,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);

        Stream stream = estimatedRowCount > _options.RowCountThreshold ? CreateTempFileStream() : new MemoryStream();

        try {
            if (format == ExcelFormat.Csv) {
                var headers = columns.Select(column => column.Header).ToList();
                var projected = rows.Select(row => (IReadOnlyList<object?>)columns.Select(column => column.ValueSelector(row)).ToList());
                await CsvExcelSerializer.WriteAsync(stream, headers, projected, delimiter, cancellationToken);
            }
            else {
                var data = rows.Select(row => {
                    var record = new Dictionary<string, object?>(columns.Count);
                    foreach (var column in columns) {
                        record[column.Header] = column.ValueSelector(row);
                    }

                    return record;
                });

                await stream.SaveAsAsync(data, sheetName: _options.SheetName, excelType: ExcelType.XLSX, cancellationToken: cancellationToken);
            }

            stream.Position = 0;
            return new ExcelResult(stream, format, SnakeCaseSanitizer.Sanitize(fileName));
        }
        catch {
            await stream.DisposeAsync();
            throw;
        }
    }

    private static FileStream CreateTempFileStream() {
        return new FileStream(
            Path.GetTempFileName(),
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            FileStreamBufferSize,
            FileOptions.DeleteOnClose);
    }
}
