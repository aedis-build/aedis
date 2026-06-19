namespace Aedis.Excel.Abstractions;

/// <summary>
///     Formato de saída suportado pelo writer de planilhas. Determina a extensão, o content type
///     e se o delimitador de CSV é aplicado.
/// </summary>
public enum ExcelFormat
{
    /// <summary>Planilha Office Open XML (.xlsx).</summary>
    Xlsx = 0,

    /// <summary>Texto separado por delimitador (.csv).</summary>
    Csv = 1
}
