namespace Aedis.Pdf.Abstractions;

/// <summary>Tipo de código gráfico a renderizar no documento.</summary>
public enum PdfCodeKind
{
    QrCode,
    Barcode
}

/// <summary>
///     Descreve um código (QR ou barras) a ser renderizado — o "quê" (tipo + conteúdo),
///     não o "como". O provider de PDF faz a renderização concreta.
/// </summary>
public sealed record PdfCode(PdfCodeKind Kind, string Content);
