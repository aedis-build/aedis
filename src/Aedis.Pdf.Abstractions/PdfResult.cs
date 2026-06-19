namespace Aedis.Pdf.Abstractions;

/// <summary>
///     Resultado de uma escrita de PDF: o stream do documento e os metadados de resposta HTTP.
/// </summary>
public sealed class PdfResult : IAsyncDisposable
{
    /// <summary>
    ///     Cria o resultado derivando o cabeçalho Content-Disposition a partir do nome de arquivo já sanitizado.
    /// </summary>
    /// <param name="stream">Stream com o PDF gerado, posicionado para leitura.</param>
    /// <param name="sanitizedFileName">Nome de arquivo já sanitizado, sem extensão.</param>
    public PdfResult(Stream stream, string sanitizedFileName) {
        Stream = stream;
        ContentDisposition = $"attachment; filename={sanitizedFileName}.pdf";
    }

    /// <summary>Stream com o conteúdo do PDF, posicionado para leitura.</summary>
    public Stream Stream { get; }

    /// <summary>Content type HTTP do documento (sempre "application/pdf").</summary>
    public string ContentType => "application/pdf";

    /// <summary>Cabeçalho Content-Disposition já com o nome de arquivo sanitizado e a extensão .pdf.</summary>
    public string ContentDisposition { get; }

    /// <summary>Descarta o <see cref="Stream" /> subjacente.</summary>
    public async ValueTask DisposeAsync() => await Stream.DisposeAsync();
}
