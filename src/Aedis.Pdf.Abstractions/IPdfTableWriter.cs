namespace Aedis.Pdf.Abstractions;

/// <summary>
///     Contrato agnóstico de motor para gerar um PDF tabular a partir de linhas e colunas.
///     O código de aplicação depende apenas desta interface — não do engine de renderização.
/// </summary>
public interface IPdfTableWriter
{
    /// <summary>
    ///     Renderiza <paramref name="rows" /> nas <paramref name="columns" /> informadas como um PDF tabular
    ///     e devolve o resultado como stream pronto para download. Use ao exportar relatórios; cabeçalho,
    ///     rodapé e demais ornamentos vêm de <paramref name="pageOptions" />.
    /// </summary>
    /// <typeparam name="T">Tipo de cada linha de dados.</typeparam>
    /// <param name="rows">Sequência de linhas a renderizar.</param>
    /// <param name="columns">Colunas na ordem de saída, cada uma com cabeçalho, seletor e largura relativa.</param>
    /// <param name="fileName">Nome de arquivo desejado (sem extensão); a implementação o sanitiza.</param>
    /// <param name="pageOptions">Opções de página/documento; quando nulo, usa os padrões.</param>
    /// <param name="cancellationToken">Token para cancelar a operação.</param>
    /// <returns>Resultado com o stream do PDF e os metadados de resposta HTTP.</returns>
    Task<PdfResult> WriteAsync<T>(
        IEnumerable<T> rows,
        IReadOnlyList<PdfColumn<T>> columns,
        string fileName,
        PdfPageOptions? pageOptions = null,
        CancellationToken cancellationToken = default);
}
