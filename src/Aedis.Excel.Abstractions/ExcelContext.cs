namespace Aedis.Excel.Abstractions;

public class ExcelContext
{
    public IEnumerable<Dictionary<string, object?>> Rows { get; set; } = [];
    public ExcelMode Mode { get; set; }
    public ExcelFormat Format { get; set; }
    public CsvDelimiter Delimiter { get; set; } = CsvDelimiter.Comma;
    public string SheetName { get; set; } = "Sheet1";
    public Stream? Result { get; set; }
}
