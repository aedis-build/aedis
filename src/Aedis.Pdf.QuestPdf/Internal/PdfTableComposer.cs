using System.Globalization;
using Aedis.Pdf.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Aedis.Pdf.QuestPdf.Internal;

/// <summary>
///     Compõe o documento PDF de tabela: página (tamanho/orientação/margens), cabeçalho (logo, classificação,
///     código, títulos), conteúdo (tabela zebrada com larguras relativas) e rodapé (numeração, textos e
///     carimbo de geração em UTC). Toda a renderização QuestPDF vive aqui, isolada do contrato.
/// </summary>
internal static class PdfTableComposer {
    public static void Compose<T>(IDocumentContainer container, IReadOnlyList<T> rows, IReadOnlyList<PdfColumn<T>> columns, PdfPageOptions options) {
        container.Page(page => {
            page.Size(PdfPageSizeMapper.Map(options.PageSize, options.Landscape));
            page.Margin(1, Unit.Centimetre);
            page.DefaultTextStyle(style => style.FontSize(9));

            page.Header().Element(header => ComposeHeader(header, options));
            page.Content().Element(content => ComposeTable(content, rows, columns));
            page.Footer().Element(footer => ComposeFooter(footer, options));
        });
    }

    private static void ComposeHeader(IContainer container, PdfPageOptions options) {
        container.PaddingBottom(8).Column(column => {
            column.Item().BorderBottom(0.5f).BorderColor(Colors.Black).PaddingBottom(6).Row(row => {
                if (!string.IsNullOrWhiteSpace(options.LogoBase64)) {
                    row.ConstantItem(80).Image(Convert.FromBase64String(options.LogoBase64));
                }

                row.RelativeItem().AlignRight().Column(right => {
                    right.Item().AlignRight().Text(options.DocumentClassification.ToUpperInvariant()).Bold().FontSize(8);

                    if (options.Code is not null) {
                        var width = options.Code.Kind == PdfCodeKind.QrCode ? 64f : 130f;
                        right.Item().PaddingTop(4).AlignRight().Width(width).Image(PdfCodeImage.Create(options.Code));
                    }
                });
            });

            if (options.Title is null && options.Subtitle is null && options.Description is null) {
                return;
            }

            column.Item().PaddingTop(6).Column(titles => {
                if (options.Title is not null) {
                    titles.Item().AlignCenter().Text(options.Title).Bold().FontSize(14);
                }

                if (options.Subtitle is not null) {
                    titles.Item().AlignCenter().Text(options.Subtitle).FontSize(10);
                }

                if (options.Description is not null) {
                    titles.Item().AlignCenter().Text(options.Description).Italic().FontSize(8);
                }
            });
        });
    }

    private static void ComposeTable<T>(IContainer container, IReadOnlyList<T> rows, IReadOnlyList<PdfColumn<T>> columns) {
        container.Table(table => {
            table.ColumnsDefinition(definition => {
                foreach (var column in columns) {
                    definition.RelativeColumn(column.RelativeWidth);
                }
            });

            table.Header(header => {
                foreach (var column in columns) {
                    header.Cell()
                        .Background(Colors.Grey.Lighten2)
                        .Border(0.5f).BorderColor(Colors.Grey.Medium)
                        .Padding(4)
                        .Text(column.Header).Bold().FontSize(8);
                }
            });

            for (var index = 0; index < rows.Count; index++) {
                var background = index % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                foreach (var column in columns) {
                    table.Cell()
                        .Background(background)
                        .Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                        .Padding(4)
                        .Text(PdfValueFormatter.Format(column.ValueSelector(rows[index]))).FontSize(8);
                }
            }
        });
    }

    private static void ComposeFooter(IContainer container, PdfPageOptions options) {
        container.BorderTop(0.5f).BorderColor(Colors.Black).PaddingTop(4).Column(column => {
            if (options.ShowPageNumbers) {
                column.Item().AlignRight().Text(text => {
                    text.CurrentPageNumber().FontSize(7);
                    text.Span(" / ").FontSize(7);
                    text.TotalPages().FontSize(7);
                });
            }

            if (options.FooterCenterText is not null) {
                column.Item().AlignCenter().Text(options.FooterCenterText).Italic().FontSize(7);
            }

            column.Item().Row(row => {
                row.RelativeItem().Text(options.FooterLeftText ?? string.Empty).Italic().FontSize(7);
                row.RelativeItem().AlignRight().Text(text => {
                    text.Span("Generated at ").FontSize(7);
                    text.Span(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)).Bold().FontSize(7);
                });
            });
        });
    }
}
