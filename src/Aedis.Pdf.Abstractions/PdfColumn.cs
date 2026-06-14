namespace Aedis.Pdf.Abstractions;

/// <summary>
///     Define uma coluna de uma tabela PDF: cabeçalho, seletor de valor por linha e largura relativa.
/// </summary>
public sealed record PdfColumn<T>(string Header, Func<T, object?> ValueSelector, float RelativeWidth = 1f);
