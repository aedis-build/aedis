namespace Aedis.Excel.Abstractions;

/// <summary>
///     Estado de uma operação de escrita de planilha: dados de entrada, formato e o stream resultante.
///     Serve de veículo entre as etapas internas do writer — as linhas já chegam como dicionários
///     cabeçalho-valor e o <see cref="Result" /> é preenchido ao final.
/// </summary>
public class ExcelContext
{
    /// <summary>Linhas a escrever, cada uma como um mapa de cabeçalho para valor de célula.</summary>
    public IEnumerable<Dictionary<string, object?>> Rows { get; set; } = [];

    /// <summary>Estratégia de buffer (memória ou arquivo temporário) escolhida para a escrita.</summary>
    public ExcelMode Mode { get; set; }

    /// <summary>Formato de saída desejado (XLSX ou CSV).</summary>
    public ExcelFormat Format { get; set; }

    /// <summary>Delimitador aplicado apenas quando o formato é CSV.</summary>
    public CsvDelimiter Delimiter { get; set; } = CsvDelimiter.Comma;

    /// <summary>Nome da aba (apenas para XLSX).</summary>
    public string SheetName { get; set; } = "Sheet1";

    /// <summary>Stream com o conteúdo gerado; preenchido ao final da escrita.</summary>
    public Stream? Result { get; set; }
}
