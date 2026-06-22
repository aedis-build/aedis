namespace Aedis.Barcode.Abstractions;

/// <summary>
///     Simbologia (tipo) do código a gerar. Define o algoritmo de codificação e a forma do desenho.
/// </summary>
public enum BarcodeSymbology {
    /// <summary>QR Code (matriz 2D), para URLs, payloads e leitura por câmera.</summary>
    QrCode,

    /// <summary>Código de barras linear Code 128, alfanumérico de alta densidade.</summary>
    Code128
}
