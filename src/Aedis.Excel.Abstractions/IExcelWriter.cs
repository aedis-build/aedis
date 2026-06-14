namespace Aedis.Excel.Abstractions;

public interface IExcelWriter
{
    Task<ExcelResult> WriteAsync<T>(
        IEnumerable<T> rows,
        IReadOnlyList<ExcelColumn<T>> columns,
        string fileName,
        ExcelFormat format = ExcelFormat.Xlsx,
        CsvDelimiter delimiter = CsvDelimiter.Comma,
        int estimatedRowCount = 0,
        CancellationToken cancellationToken = default);
}
