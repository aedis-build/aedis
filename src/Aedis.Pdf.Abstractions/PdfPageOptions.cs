namespace Aedis.Pdf.Abstractions;

/// <summary>
///     Opções de página/documento agnósticas de motor para a escrita de PDF.
///     Não referencia nenhum tipo do engine de renderização.
/// </summary>
public sealed class PdfPageOptions
{
    public PdfPageSize PageSize { get; init; } = PdfPageSize.A4;
    public bool Landscape { get; init; }
    public string? LogoBase64 { get; init; }
    public string LogoMimeType { get; init; } = "image/png";
    public string DocumentClassification { get; init; } = "INTERNO";
    public string? Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? FooterLeftText { get; init; }
    public string? FooterCenterText { get; init; }
    public bool ShowPageNumbers { get; init; }

    /// <summary>Código (QR/barras) opcional a renderizar; descrito de forma neutra (ver <see cref="PdfCode" />).</summary>
    public PdfCode? Code { get; init; }
}
