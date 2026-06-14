namespace Aedis.Excel.Abstractions;

public sealed class ExcelWriterOptions
{
    public int RowCountThreshold { get; set; } = 500;
    public string SheetName { get; set; } = "Sheet1";
}
