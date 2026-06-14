namespace Aedis.Excel.Abstractions;

public sealed class ExcelResult : IAsyncDisposable
{
    public Stream Stream { get; }
    public ExcelFormat Format { get; }

    public string ContentType => Format == ExcelFormat.Csv
        ? "text/csv"
        : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public string ContentDisposition { get; }

    internal ExcelResult(Stream stream, ExcelFormat format, string sanitizedFileName)
    {
        Stream = stream;
        Format = format;
        var extension = format == ExcelFormat.Csv ? "csv" : "xlsx";
        ContentDisposition = $"attachment; filename={sanitizedFileName}.{extension}";
    }

    public async ValueTask DisposeAsync() => await Stream.DisposeAsync();
}
