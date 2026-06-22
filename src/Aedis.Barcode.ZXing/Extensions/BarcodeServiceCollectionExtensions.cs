using Aedis.Barcode.Abstractions;
using Aedis.Barcode.ZXing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro do gerador de código de barras/QR do Aedis (ZXing). Expõe <see cref="IBarcodeGenerator" />
///     agnóstico, reaproveitável por qualquer camada (HTTP, e-mail, PDF, etiquetas) — não acoplado a PDF.
/// </summary>
public static class BarcodeServiceCollectionExtensions {
    /// <summary>
    ///     Registra o <see cref="IBarcodeGenerator" /> sobre ZXing (idempotente).
    /// </summary>
    /// <param name="services">Coleção de serviços.</param>
    public static IServiceCollection AddAedisBarcode(this IServiceCollection services) {
        services.TryAddSingleton<IBarcodeGenerator, ZXingBarcodeGenerator>();
        return services;
    }
}
