using Aedis.Pdf.Abstractions;
using Aedis.Pdf.QuestPdf.Internal;
using FluentAssertions;
using QuestPDF.Helpers;
using Xunit;

namespace Aedis.Pdf.QuestPdf.Tests;

/// <summary>Formatação de valores de célula e mapeamento de tamanhos de página (com paisagem).</summary>
public sealed class PdfInternalsTests
{
    public static TheoryData<object?, string> Cases => new() {
        { null, "" },
        { true, "true" },
        { false, "false" },
        { new DateTime(2026, 10, 16), "2026-10-16" },
        { new DateTime(2026, 10, 16, 13, 45, 0), "2026-10-16 13:45" },
        { new DateTimeOffset(2026, 10, 16, 13, 45, 0, TimeSpan.Zero), "2026-10-16 13:45" },
        { new DateOnly(2026, 10, 16), "2026-10-16" },
        { new TimeOnly(13, 45), "13:45" },
        { "texto", "texto" }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Format_converte_cada_tipo(object? value, string expected) {
        PdfValueFormatter.Format(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(PdfPageSize.A3)]
    [InlineData(PdfPageSize.A4)]
    [InlineData(PdfPageSize.A5)]
    [InlineData(PdfPageSize.Letter)]
    [InlineData(PdfPageSize.Legal)]
    public void Map_resolve_o_tamanho_e_a_paisagem_inverte_os_lados(PdfPageSize size) {
        var portrait = PdfPageSizeMapper.Map(size, false);
        var landscape = PdfPageSizeMapper.Map(size, true);

        portrait.Height.Should().BeGreaterThan(portrait.Width);
        landscape.Width.Should().Be(portrait.Height);
        landscape.Height.Should().Be(portrait.Width);
    }

    [Fact]
    public void A4_e_o_padrao_para_tamanho_desconhecido() {
        PdfPageSizeMapper.Map((PdfPageSize)99, false).Width.Should().Be(PageSizes.A4.Width);
    }
}
