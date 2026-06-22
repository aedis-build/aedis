using System.Text;
using Aedis.Barcode.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Barcode.ZXing.Tests;

/// <summary>
///     Garante que o gerador produz PNG válido (assinatura) e SVG vetorial para QR Code e Code 128, rejeita
///     conteúdo vazio e é resolvido pelo registro de DI.
/// </summary>
public sealed class ZXingBarcodeGeneratorTests {
    private static readonly IBarcodeGenerator Generator = new ZXingBarcodeGenerator();

    [Theory]
    [InlineData(BarcodeSymbology.QrCode)]
    [InlineData(BarcodeSymbology.Code128)]
    public void Png_tem_assinatura_valida(BarcodeSymbology symbology) {
        var bytes = Generator.CreatePng("AEDIS-123", symbology);

        bytes.Length.Should().BeGreaterThan(8);
        bytes[0].Should().Be(0x89);
        Encoding.ASCII.GetString(bytes, 1, 3).Should().Be("PNG");
    }

    [Theory]
    [InlineData(BarcodeSymbology.QrCode)]
    [InlineData(BarcodeSymbology.Code128)]
    public void Svg_e_vetorial(BarcodeSymbology symbology) {
        var svg = Generator.CreateSvg("AEDIS-123", symbology);

        svg.Should().StartWith("<svg").And.Contain("</svg>").And.Contain("<rect");
    }

    [Fact]
    public void Conteudo_vazio_lanca() {
        var act = () => Generator.CreatePng(string.Empty, BarcodeSymbology.QrCode);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddAedisBarcode_resolve_o_gerador() {
        using var provider = new ServiceCollection().AddAedisBarcode().BuildServiceProvider();

        provider.GetRequiredService<IBarcodeGenerator>().Should().BeOfType<ZXingBarcodeGenerator>();
    }
}
