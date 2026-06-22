using Aedis.Core.Utils;
using Aedis.Pdf.Abstractions;
using Aedis.Pdf.QuestPdf.Internal;
using QuestPDF.Fluent;

namespace Aedis.Pdf.QuestPdf;

/// <summary>
///     Implementação de <see cref="IPdfTableWriter" /> sobre o QuestPDF. Renderiza as linhas em uma tabela
///     paginada (com cabeçalho, rodapé e opções de página) e devolve o PDF em um <see cref="PdfResult" /> com
///     o nome de arquivo sanitizado.
/// </summary>
public sealed class PdfTableWriter : IPdfTableWriter {
    /// <inheritdoc />
    public Task<PdfResult> WriteAsync<T>(
        IEnumerable<T> rows,
        IReadOnlyList<PdfColumn<T>> columns,
        string fileName,
        PdfPageOptions? pageOptions = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);

        var options = pageOptions ?? new PdfPageOptions();
        var materialized = rows as IReadOnlyList<T> ?? rows.ToList();

        var document = Document.Create(container => PdfTableComposer.Compose(container, materialized, columns, options));

        var stream = new MemoryStream();
        document.GeneratePdf(stream);
        stream.Position = 0;

        return Task.FromResult(new PdfResult(stream, SnakeCaseSanitizer.Sanitize(fileName)));
    }
}
