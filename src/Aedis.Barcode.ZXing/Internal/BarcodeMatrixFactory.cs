using Aedis.Barcode.Abstractions;
using ZXing;
using ZXing.Common;

namespace Aedis.Barcode.ZXing.Internal;

/// <summary>
///     Produz a matriz de bits do código via ZXing, em duas resoluções: por pixel (para raster/PNG, escalada
///     ao tamanho pedido) e natural (para vetor/SVG, um bit por módulo).
/// </summary>
internal static class BarcodeMatrixFactory {
    public static BitMatrix CreatePixel(string content, BarcodeSymbology symbology, BarcodeOptions options) {
        var (format, defaultWidth, defaultHeight) = Defaults(symbology);
        var width = options.Width > 0 ? options.Width : defaultWidth;
        var height = options.Height > 0 ? options.Height : defaultHeight;
        return Encode(content, format, width, height, options.Margin);
    }

    public static BitMatrix CreateNatural(string content, BarcodeSymbology symbology, BarcodeOptions options) {
        var format = symbology == BarcodeSymbology.QrCode ? BarcodeFormat.QR_CODE : BarcodeFormat.CODE_128;
        var height = symbology == BarcodeSymbology.QrCode ? 0 : 1;
        return Encode(content, format, 0, height, options.Margin);
    }

    private static BitMatrix Encode(string content, BarcodeFormat format, int width, int height, int margin) {
        if (string.IsNullOrEmpty(content)) {
            throw new ArgumentException("O conteúdo do código não pode ser vazio.", nameof(content));
        }

        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = margin };
        return new MultiFormatWriter().encode(content, format, width, height, hints);
    }

    private static (BarcodeFormat Format, int Width, int Height) Defaults(BarcodeSymbology symbology) =>
        symbology == BarcodeSymbology.QrCode
            ? (BarcodeFormat.QR_CODE, 200, 200)
            : (BarcodeFormat.CODE_128, 360, 100);
}
