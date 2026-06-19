namespace Aedis.Excel.Abstractions;

/// <summary>
///     Delimitador de campos usado ao gerar CSV. Aplica-se apenas quando o formato de saída é
///     <see cref="ExcelFormat.Csv" />; é ignorado para XLSX.
/// </summary>
public enum CsvDelimiter
{
    /// <summary>Vírgula (',') — padrão e mais comum.</summary>
    Comma = 0,

    /// <summary>Ponto e vírgula (';') — comum em locales onde a vírgula é separador decimal.</summary>
    Semicolon = 1,

    /// <summary>Tabulação ('\t').</summary>
    Tab = 2
}
