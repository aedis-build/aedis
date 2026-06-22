using Aedis.Excel.Abstractions;
using Aedis.Excel.MiniExcel;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro do provider de Excel do Aedis (MiniExcel). Expõe <see cref="IExcelWriter" /> agnóstico para o
///     código de aplicação — quem consome depende só do contrato, não do MiniExcel.
/// </summary>
public static class ExcelServiceCollectionExtensions {
    /// <summary>
    ///     Registra o <see cref="IExcelWriter" /> sobre MiniExcel e suas opções.
    /// </summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configure">Ajuste opcional das opções (limiar de linhas, nome da aba).</param>
    public static IServiceCollection AddAedisExcel(this IServiceCollection services, Action<ExcelWriterOptions>? configure = null) {
        if (configure is null) {
            services.AddOptions<ExcelWriterOptions>();
        }
        else {
            services.Configure(configure);
        }

        services.AddSingleton<IExcelWriter, ExcelWriter>();
        return services;
    }
}
