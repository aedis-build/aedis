namespace Aedis.Pdf.Abstractions;

/// <summary>
///     Tamanho de página neutro de motor. O provider de PDF mapeia para o tamanho concreto do engine.
/// </summary>
public enum PdfPageSize
{
    A3,
    A4,
    A5,
    Letter,
    Legal
}
