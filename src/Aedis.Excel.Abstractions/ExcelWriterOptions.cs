namespace Aedis.Excel.Abstractions;

/// <summary>
///     Opções de configuração do writer de planilhas, normalmente vinculadas via configuração da aplicação.
///     Define o limiar de linhas que decide entre buffer em memória e arquivo temporário, e o nome padrão da aba.
/// </summary>
public sealed class ExcelWriterOptions
{
    /// <summary>
    ///     Número de linhas a partir do qual a escrita passa a usar arquivo temporário em vez de memória.
    ///     Padrão: 500.
    /// </summary>
    public int RowCountThreshold { get; set; } = 500;

    /// <summary>Nome padrão da aba em arquivos XLSX. Padrão: "Sheet1".</summary>
    public string SheetName { get; set; } = "Sheet1";
}
