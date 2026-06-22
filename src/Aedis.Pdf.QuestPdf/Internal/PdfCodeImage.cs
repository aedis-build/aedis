using Aedis.Pdf.Abstractions;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Aedis.Pdf.QuestPdf.Internal;

/// <summary>
///     Gera a imagem PNG de um <see cref="PdfCode" /> (QR Code ou código de barras Code 128) via ZXing, para
///     ser embutida no cabeçalho do PDF.
/// </summary>
internal static class PdfCodeImage {
    public static byte[] Create(PdfCode code) {
        var (format, width, height) = code.Kind == PdfCodeKind.QrCode
            ? (BarcodeFormat.QR_CODE, 200, 200)
            : (BarcodeFormat.CODE_128, 360, 100);

        var writer = new MultiFormatWriter();
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 1 };
        var matrix = writer.encode(code.Content, format, width, height, hints);

        using var bitmap = new SKBitmap(matrix.Width, matrix.Height);
        for (var y = 0; y < matrix.Height; y++) {
            for (var x = 0; x < matrix.Width; x++) {
                bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
