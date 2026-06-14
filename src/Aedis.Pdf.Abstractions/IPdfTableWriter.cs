namespace Aedis.Pdf.Abstractions;

/// <summary>
///     Contrato agnóstico de motor para gerar um PDF tabular a partir de linhas e colunas.
///     O código de aplicação depende apenas desta interface — não do engine de renderização.
/// </summary>
public interface IPdfTableWriter
{
    Task<PdfResult> WriteAsync<T>(
        IEnumerable<T> rows,
        IReadOnlyList<PdfColumn<T>> columns,
        string fileName,
        PdfPageOptions? pageOptions = null,
        CancellationToken cancellationToken = default);
}
