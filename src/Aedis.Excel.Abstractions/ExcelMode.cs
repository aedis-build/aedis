namespace Aedis.Excel.Abstractions;

/// <summary>
///     Estratégia de buffer da escrita: manter o conteúdo em memória ou usar um arquivo temporário.
///     A implementação costuma escolher conforme o volume estimado de linhas para equilibrar
///     consumo de memória e desempenho.
/// </summary>
public enum ExcelMode
{
    /// <summary>Bufferiza o conteúdo inteiro em memória; rápido, indicado para volumes pequenos.</summary>
    Memory = 0,

    /// <summary>Escreve em um arquivo temporário em disco; reduz uso de memória para volumes grandes.</summary>
    TempFile = 1
}
