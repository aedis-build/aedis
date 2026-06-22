namespace Aedis.Barcode.Abstractions;

/// <summary>
///     Gera imagens de código de barras e QR Code de forma agnóstica ao consumidor. O código de aplicação
///     depende apenas deste contrato — não da biblioteca de codificação. Use a saída PNG (raster, para embutir
///     em HTML/e-mail/PDF) ou SVG (vetor, nítido em qualquer escala).
/// </summary>
public interface IBarcodeGenerator {
    /// <summary>
    ///     Gera o código como imagem PNG (bytes).
    /// </summary>
    /// <param name="content">Conteúdo a codificar.</param>
    /// <param name="symbology">Simbologia (QR Code ou Code 128).</param>
    /// <param name="options">Opções de tamanho/margem; quando nulo, usa os padrões.</param>
    byte[] CreatePng(string content, BarcodeSymbology symbology, BarcodeOptions? options = null);

    /// <summary>
    ///     Gera o código como documento SVG (texto vetorial).
    /// </summary>
    /// <param name="content">Conteúdo a codificar.</param>
    /// <param name="symbology">Simbologia (QR Code ou Code 128).</param>
    /// <param name="options">Opções de tamanho de módulo/margem; quando nulo, usa os padrões.</param>
    string CreateSvg(string content, BarcodeSymbology symbology, BarcodeOptions? options = null);
}
