using QuestPDF.Infrastructure;

namespace Aedis.Pdf.QuestPdf;

/// <summary>
///     Opções do provider de PDF. Por padrão usa a licença <see cref="LicenseType.Community" /> do QuestPDF
///     (gratuita para a maioria dos cenários). Ajuste para a licença adequada ao seu uso comercial.
/// </summary>
public sealed class PdfWriterOptions {
    /// <summary>Tipo de licença do QuestPDF aplicado globalmente no registro.</summary>
    public LicenseType License { get; set; } = LicenseType.Community;
}
