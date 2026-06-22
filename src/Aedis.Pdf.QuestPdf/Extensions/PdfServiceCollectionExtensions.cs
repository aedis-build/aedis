using Aedis.Pdf.Abstractions;
using Aedis.Pdf.QuestPdf;
using QuestPDF;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro do provider de PDF do Aedis (QuestPDF). Expõe <see cref="IPdfTableWriter" /> agnóstico e aplica
///     a licença do QuestPDF (global) conforme as opções.
/// </summary>
public static class PdfServiceCollectionExtensions {
    /// <summary>
    ///     Registra o <see cref="IPdfTableWriter" /> sobre QuestPDF e configura a licença.
    /// </summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configure">Ajuste opcional das opções (tipo de licença).</param>
    public static IServiceCollection AddAedisPdf(this IServiceCollection services, Action<PdfWriterOptions>? configure = null) {
        var options = new PdfWriterOptions();
        configure?.Invoke(options);

        Settings.License = options.License;

        services.AddAedisBarcode();
        services.AddSingleton<IPdfTableWriter, PdfTableWriter>();
        return services;
    }
}
