using Aedis.Excel.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Excel.MiniExcel.Tests;

/// <summary>
///     Exercita o <see cref="ExcelWriter" /> de ponta a ponta: gera XLSX (round-trip pelo MiniExcel), CSV com
///     delimitador e escaping, e o caminho de arquivo temporário para volumes grandes. Também valida o nome
///     sanitizado no <c>Content-Disposition</c>.
/// </summary>
public sealed class ExcelWriterTests {
    private sealed record Person(string Name, int Age);

    private static readonly IReadOnlyList<ExcelColumn<Person>> Columns = [
        new ExcelColumn<Person>("Name", person => person.Name),
        new ExcelColumn<Person>("Age", person => person.Age)
    ];

    private static readonly Person[] Rows = [new Person("Ana", 30), new Person("Bob", 25)];

    private static ExcelWriter CreateWriter(int threshold = 500) =>
        new(Options.Create(new ExcelWriterOptions { RowCountThreshold = threshold }));

    [Fact]
    public async Task Gera_xlsx_com_round_trip() {
        await using var result = await CreateWriter().WriteAsync(Rows, Columns, "My Export.xlsx");

        result.Format.Should().Be(ExcelFormat.Xlsx);
        result.ContentDisposition.Should().Contain("my_export.xlsx");
        result.Stream.Length.Should().BeGreaterThan(0);

        var back = MiniExcelLibs.MiniExcel.Query(result.Stream, useHeaderRow: true).Cast<IDictionary<string, object>>().ToList();
        back.Should().HaveCount(2);
        ((string)back[0]["Name"]).Should().Be("Ana");
        ((string)back[1]["Name"]).Should().Be("Bob");
    }

    [Fact]
    public async Task Gera_csv_com_delimitador() {
        await using var result = await CreateWriter().WriteAsync(Rows, Columns, "data", ExcelFormat.Csv, CsvDelimiter.Semicolon);

        result.Format.Should().Be(ExcelFormat.Csv);
        result.ContentType.Should().Be("text/csv");

        using var reader = new StreamReader(result.Stream);
        var text = await reader.ReadToEndAsync();
        text.Should().Contain("Name;Age");
        text.Should().Contain("Ana;30");
    }

    [Fact]
    public async Task Csv_escapa_valor_com_delimitador() {
        var rows = new[] { new Person("a;b", 1) };
        await using var result = await CreateWriter().WriteAsync(rows, Columns, "x", ExcelFormat.Csv, CsvDelimiter.Semicolon);

        using var reader = new StreamReader(result.Stream);
        var text = await reader.ReadToEndAsync();
        text.Should().Contain("\"a;b\"");
    }

    [Fact]
    public async Task Acima_do_limiar_usa_arquivo_temporario() {
        await using var result = await CreateWriter(threshold: 1).WriteAsync(Rows, Columns, "big", estimatedRowCount: 100);

        result.Stream.Should().BeOfType<FileStream>();
        result.Stream.Length.Should().BeGreaterThan(0);
    }
}
