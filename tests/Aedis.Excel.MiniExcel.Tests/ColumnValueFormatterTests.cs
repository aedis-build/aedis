using Aedis.Excel.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Excel.MiniExcel.Tests;

/// <summary>Formatação de células: nulos, booleanos, datas e horas em ISO invariante; demais valores via ToString.</summary>
public sealed class ColumnValueFormatterTests
{
    public static TheoryData<object?, string> Cases => new() {
        { null, "" },
        { true, "true" },
        { false, "false" },
        { new DateTime(2026, 10, 16), "2026-10-16" },
        { new DateTime(2026, 10, 16, 13, 45, 10), "2026-10-16T13:45:10" },
        { new DateTimeOffset(2026, 10, 16, 13, 45, 10, TimeSpan.FromHours(-3)), "2026-10-16T13:45:10-03:00" },
        { new DateOnly(2026, 10, 16), "2026-10-16" },
        { new TimeOnly(13, 45, 10), "13:45:10" },
        { 12.5m, "12.5" },
        { "texto", "texto" }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Format_converte_cada_tipo(object? value, string expected) {
        ColumnValueFormatter.Format(value).Should().Be(expected);
    }

    [Fact]
    public void ExcelContext_tem_defaults_de_planilha_e_delimitador() {
        var context = new ExcelContext { Mode = ExcelMode.Memory, Format = ExcelFormat.Csv };

        context.SheetName.Should().Be("Sheet1");
        context.Delimiter.Should().Be(CsvDelimiter.Comma);
        context.Rows.Should().BeEmpty();
        context.Result.Should().BeNull();
        context.Mode.Should().Be(ExcelMode.Memory);
        context.Format.Should().Be(ExcelFormat.Csv);
    }
}
