namespace Aedis.Barcode.Abstractions;

/// <summary>
///     Opções de geração do código. <see cref="Width" />/<see cref="Height" /> valem para a saída raster (PNG);
///     <see cref="ModuleSize" /> para a saída vetorial (SVG). Zero significa o tamanho natural/padrão da
///     simbologia.
/// </summary>
public sealed class BarcodeOptions {
    /// <summary>Largura alvo em pixels do PNG. Zero usa o padrão da simbologia.</summary>
    public int Width { get; init; }

    /// <summary>Altura alvo em pixels do PNG (e altura das barras no SVG do Code 128). Zero usa o padrão.</summary>
    public int Height { get; init; }

    /// <summary>Margem (quiet zone) em módulos ao redor do código.</summary>
    public int Margin { get; init; } = 1;

    /// <summary>Tamanho de cada módulo em pixels na saída SVG. Padrão 4.</summary>
    public int ModuleSize { get; init; } = 4;
}
