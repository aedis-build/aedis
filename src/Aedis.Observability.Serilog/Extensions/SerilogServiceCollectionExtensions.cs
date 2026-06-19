using Aedis.Observability.Serilog;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do logging Serilog do Aedis.
/// </summary>
public static class SerilogServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o Serilog como provider de logging com a configuração padrão do Aedis: Console (stdout,
    ///     entrega durável) sempre e sink OTLP em lote quando a seção <c>Telemetry</c> tem um endpoint.
    ///     Substitui os providers de logging existentes.
    /// </summary>
    public static IServiceCollection AddAedisSerilog(this IServiceCollection services,
        IConfiguration configuration) {
        return services.AddSerilog(loggerConfiguration => AedisSerilog.Configure(loggerConfiguration, configuration));
    }
}
