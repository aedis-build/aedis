namespace Aedis.Pdf.Abstractions;

/// <summary>
///     Tamanho de página neutro de motor. O provider de PDF mapeia para o tamanho concreto do engine.
/// </summary>
public enum PdfPageSize
{
    /// <summary>ISO A3 (297 × 420 mm).</summary>
    A3,

    /// <summary>ISO A4 (210 × 297 mm) — tamanho padrão.</summary>
    A4,

    /// <summary>ISO A5 (148 × 210 mm).</summary>
    A5,

    /// <summary>US Letter (8.5 × 11 pol).</summary>
    Letter,

    /// <summary>US Legal (8.5 × 14 pol).</summary>
    Legal
}
