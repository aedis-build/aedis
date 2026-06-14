namespace Aedis.Excel.Abstractions;

public sealed record ExcelColumn<T>(string Header, Func<T, object?> ValueSelector);
