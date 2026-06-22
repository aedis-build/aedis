using Aedis.Hosting.AspNetCore.RequestLogging;
using Aedis.Observability.Serilog;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Liga o access-log estruturado de requisições do Aedis (uma linha por requisição: método, rota, status,
///     tempo), substituindo o logging de requisição ruidoso e em múltiplas linhas do ASP.NET. O enriquecimento
///     é seguro por construção (sem <c>Authorization</c>, com query string ofuscada). Opt-out por
///     <c>Logging:RequestLogging:Enabled = false</c>.
/// </summary>
public static class AedisRequestLoggingExtensions {
    /// <summary>
    ///     Adiciona o middleware de access-log do Aedis ao pipeline, salvo se desligado por configuração.
    /// </summary>
    /// <param name="app">Pipeline da aplicação.</param>
    /// <param name="configuration">Configuração da aplicação (seção <c>Logging:RequestLogging</c>).</param>
    public static IApplicationBuilder UseAedisRequestLogging(this IApplicationBuilder app, IConfiguration configuration) {
        var section = configuration.GetSection("Logging:RequestLogging");
        if (bool.TryParse(section["Enabled"], out var enabled) && !enabled) {
            return app;
        }

        var redaction = RedactionOptions.FromConfiguration(configuration);
        return app.UseSerilogRequestLogging(options => {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                AedisRequestLogging.Enrich(diagnosticContext, httpContext, redaction);
        });
    }
}
