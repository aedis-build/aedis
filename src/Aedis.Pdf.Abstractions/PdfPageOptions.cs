namespace Aedis.Pdf.Abstractions;

/// <summary>
///     Opções de página/documento agnósticas de motor para a escrita de PDF.
///     Não referencia nenhum tipo do engine de renderização.
/// </summary>
public sealed class PdfPageOptions
{
    /// <summary>Tamanho da página. Padrão: <see cref="PdfPageSize.A4" />.</summary>
    public PdfPageSize PageSize { get; init; } = PdfPageSize.A4;

    /// <summary>Quando verdadeiro, orienta a página em paisagem em vez de retrato.</summary>
    public bool Landscape { get; init; }

    /// <summary>Logotipo do cabeçalho codificado em Base64; nulo para omitir.</summary>
    public string? LogoBase64 { get; init; }

    /// <summary>MIME type do <see cref="LogoBase64" />. Padrão: "image/png".</summary>
    public string LogoMimeType { get; init; } = "image/png";

    /// <summary>Rótulo de classificação do documento exibido na página. Padrão: "INTERNO".</summary>
    public string DocumentClassification { get; init; } = "INTERNO";

    /// <summary>Título principal do documento; nulo para omitir.</summary>
    public string? Title { get; init; }

    /// <summary>Subtítulo exibido abaixo do título; nulo para omitir.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Texto descritivo do documento; nulo para omitir.</summary>
    public string? Description { get; init; }

    /// <summary>Texto do rodapé alinhado à esquerda; nulo para omitir.</summary>
    public string? FooterLeftText { get; init; }

    /// <summary>Texto do rodapé centralizado; nulo para omitir.</summary>
    public string? FooterCenterText { get; init; }

    /// <summary>Quando verdadeiro, exibe a numeração de páginas no rodapé.</summary>
    public bool ShowPageNumbers { get; init; }

    /// <summary>Código (QR/barras) opcional a renderizar; descrito de forma neutra (ver <see cref="PdfCode" />).</summary>
    public PdfCode? Code { get; init; }
}
