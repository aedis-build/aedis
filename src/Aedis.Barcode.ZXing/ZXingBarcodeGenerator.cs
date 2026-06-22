using System.Text;
using Aedis.Barcode.Abstractions;
using Aedis.Barcode.ZXing.Internal;
using SkiaSharp;

namespace Aedis.Barcode.ZXing;

/// <summary>
///     Implementação de <see cref="IBarcodeGenerator" /> sobre o ZXing.Net. A matriz de bits é codificada pelo
///     ZXing; o PNG é rasterizado com SkiaSharp e o SVG é montado como texto puro (sem rasterização).
/// </summary>
public sealed class ZXingBarcodeGenerator : IBarcodeGenerator {
    /// <inheritdoc />
    public byte[] CreatePng(string content, BarcodeSymbology symbology, BarcodeOptions? options = null) {
        var matrix = BarcodeMatrixFactory.CreatePixel(content, symbology, options ?? new BarcodeOptions());

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

    /// <inheritdoc />
    public string CreateSvg(string content, BarcodeSymbology symbology, BarcodeOptions? options = null) {
        var opts = options ?? new BarcodeOptions();
        var module = opts.ModuleSize > 0 ? opts.ModuleSize : 4;
        var matrix = BarcodeMatrixFactory.CreateNatural(content, symbology, opts);

        return symbology == BarcodeSymbology.QrCode
            ? BuildSvg(matrix.Width, matrix.Height, module, matrix.Width * module, matrix.Height * module)
            : BuildSvg(matrix.Width, 1, module, matrix.Width * module, opts.Height > 0 ? opts.Height : 60);

        string BuildSvg(int columns, int rows, int cellSize, int viewWidth, int viewHeight) {
            var builder = new StringBuilder();
            builder.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{viewWidth}\" height=\"{viewHeight}\" viewBox=\"0 0 {viewWidth} {viewHeight}\" shape-rendering=\"crispEdges\">");
            builder.Append($"<rect width=\"{viewWidth}\" height=\"{viewHeight}\" fill=\"#ffffff\"/>");

            var barHeight = rows == 1 ? viewHeight : cellSize;
            for (var y = 0; y < rows; y++) {
                for (var x = 0; x < columns; x++) {
                    if (matrix[x, y]) {
                        builder.Append($"<rect x=\"{x * cellSize}\" y=\"{y * cellSize}\" width=\"{cellSize}\" height=\"{barHeight}\" fill=\"#000000\"/>");
                    }
                }
            }

            return builder.Append("</svg>").ToString();
        }
    }
}
