namespace Aedis.Excel.Abstractions;

/// <summary>
///     Resultado de uma escrita de planilha: o stream do arquivo e os metadados de resposta HTTP
///     (content type e content disposition). Descarte assíncrono libera o stream subjacente —
///     prefira <c>await using</c> para garantir a liberação após o envio.
/// </summary>
public sealed class ExcelResult : IAsyncDisposable
{
    /// <summary>Stream com o conteúdo gerado, posicionado para leitura.</summary>
    public Stream Stream { get; }

    /// <summary>Formato do arquivo gerado.</summary>
    public ExcelFormat Format { get; }

    /// <summary>Content type HTTP correspondente ao <see cref="Format" /> (CSV ou XLSX).</summary>
    public string ContentType => Format == ExcelFormat.Csv
        ? "text/csv"
        : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Cabeçalho Content-Disposition já com o nome de arquivo sanitizado e a extensão.</summary>
    public string ContentDisposition { get; }

    /// <summary>
    ///     Cria o resultado derivando extensão e Content-Disposition a partir do formato e do nome
    ///     de arquivo já sanitizado. Interno: instanciado apenas pelas implementações de <see cref="IExcelWriter" />.
    /// </summary>
    internal ExcelResult(Stream stream, ExcelFormat format, string sanitizedFileName)
    {
        Stream = stream;
        Format = format;
        var extension = format == ExcelFormat.Csv ? "csv" : "xlsx";
        ContentDisposition = $"attachment; filename={sanitizedFileName}.{extension}";
    }

    /// <summary>Descarta o <see cref="Stream" /> subjacente.</summary>
    public async ValueTask DisposeAsync() => await Stream.DisposeAsync();
}
