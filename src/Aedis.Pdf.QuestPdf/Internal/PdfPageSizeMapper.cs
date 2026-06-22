using Aedis.Pdf.Abstractions;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Aedis.Pdf.QuestPdf.Internal;

/// <summary>
///     Mapeia o tamanho de página agnóstico (<see cref="PdfPageSize" />) para o <see cref="PageSize" /> do
///     QuestPDF, aplicando a orientação paisagem quando solicitada.
/// </summary>
internal static class PdfPageSizeMapper {
    public static PageSize Map(PdfPageSize pageSize, bool landscape) {
        var size = pageSize switch {
            PdfPageSize.A3 => PageSizes.A3,
            PdfPageSize.A5 => PageSizes.A5,
            PdfPageSize.Letter => PageSizes.Letter,
            PdfPageSize.Legal => PageSizes.Legal,
            _ => PageSizes.A4
        };

        return landscape ? size.Landscape() : size;
    }
}
