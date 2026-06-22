using System.Text;
using Aedis.Pdf.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Pdf.QuestPdf.Tests;

/// <summary>
///     Exercita o <see cref="PdfTableWriter" /> de ponta a ponta: gera um PDF real (valida a assinatura
///     <c>%PDF</c>), com e sem opções de página (título, QR code, numeração) — provando que o caminho de
///     cabeçalho/código/rodapé renderiza.
/// </summary>
public sealed class PdfTableWriterTests {
    private sealed record Person(string Name, int Age);

    private static readonly IReadOnlyList<PdfColumn<Person>> Columns = [
        new PdfColumn<Person>("Name", person => person.Name),
        new PdfColumn<Person>("Age", person => person.Age, 0.5f)
    ];

    private static readonly Person[] Rows = [new Person("Ana", 30), new Person("Bob", 25)];

    private static IPdfTableWriter CreateWriter() =>
        new ServiceCollection().AddAedisPdf().BuildServiceProvider().GetRequiredService<IPdfTableWriter>();

    private static async Task<byte[]> ReadAsync(Stream stream) {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task Gera_pdf_valido() {
        await using var result = await CreateWriter().WriteAsync(Rows, Columns, "Report.pdf");

        result.ContentType.Should().Be("application/pdf");
        result.ContentDisposition.Should().Contain("report.pdf");

        var bytes = await ReadAsync(result.Stream);
        bytes.Length.Should().BeGreaterThan(0);
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Gera_pdf_com_titulo_qrcode_e_numeracao() {
        var options = new PdfPageOptions {
            Title = "Relatório",
            Subtitle = "Exemplo",
            ShowPageNumbers = true,
            Landscape = true,
            Code = new PdfCode(PdfCodeKind.QrCode, "https://example.test/doc/1")
        };

        await using var result = await CreateWriter().WriteAsync(Rows, Columns, "doc", options);

        var bytes = await ReadAsync(result.Stream);
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Gera_pdf_com_barcode() {
        var options = new PdfPageOptions { Code = new PdfCode(PdfCodeKind.Barcode, "ABC123456") };

        await using var result = await CreateWriter().WriteAsync(Rows, Columns, "bar", options);

        var bytes = await ReadAsync(result.Stream);
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }
}
