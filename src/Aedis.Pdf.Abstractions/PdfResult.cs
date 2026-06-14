namespace Aedis.Pdf.Abstractions;

/// <summary>
///     Resultado de uma escrita de PDF: o stream do documento e os metadados de resposta HTTP.
/// </summary>
public sealed class PdfResult : IAsyncDisposable
{
    public PdfResult(Stream stream, string sanitizedFileName) {
        Stream = stream;
        ContentDisposition = $"attachment; filename={sanitizedFileName}.pdf";
    }

    public Stream Stream { get; }
    public string ContentType => "application/pdf";
    public string ContentDisposition { get; }

    public async ValueTask DisposeAsync() => await Stream.DisposeAsync();
}
