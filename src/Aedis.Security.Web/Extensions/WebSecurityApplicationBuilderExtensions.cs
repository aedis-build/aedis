using Aedis.Security.Web.Middleware;
using Aedis.Security.Web.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Liga, na ordem correta, a camada de segurança HTTP do Aedis registrada por <c>AddAedisWebSecurity</c>:
///     cabeçalhos encaminhados → HSTS/HTTPS-redirect → cabeçalhos de segurança → proteção de Host → rate
///     limiter. Coloque <c>UseAedisWebSecurity</c> cedo no pipeline, logo após o tratamento global de
///     exceções e antes da autenticação.
/// </summary>
public static class WebSecurityApplicationBuilderExtensions
{
    /// <summary>
    ///     Insere todo o pipeline de segurança HTTP do Aedis conforme as opções vinculadas. HSTS é omitido em
    ///     ambiente de desenvolvimento; cada controle desligado nas opções é simplesmente pulado.
    /// </summary>
    /// <param name="app">Builder do pipeline da aplicação.</param>
    /// <returns>O próprio <paramref name="app" />, para encadeamento.</returns>
    public static IApplicationBuilder UseAedisWebSecurity(this IApplicationBuilder app) {
        var options = app.ApplicationServices.GetRequiredService<IOptions<WebSecurityOptions>>().Value;
        var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();

        if (options.ForwardedHeaders.Enabled)
            app.UseForwardedHeaders();

        if (options.Https.EnableHsts && !environment.IsDevelopment())
            app.UseHsts();

        if (options.Https.EnableHttpsRedirection)
            app.UseHttpsRedirection();

        app.UseAedisSecurityHeaders();
        app.UseAedisHostHeaderProtection();

        if (options.RateLimiting.Enabled)
            app.UseRateLimiter();

        return app;
    }

    /// <summary>Adiciona apenas o middleware de cabeçalhos de segurança de resposta (uso avulso, ex.: minimal APIs).</summary>
    public static IApplicationBuilder UseAedisSecurityHeaders(this IApplicationBuilder app) {
        var headers = app.ApplicationServices.GetRequiredService<IOptions<WebSecurityOptions>>().Value.Headers;
        return app.UseMiddleware<SecurityHeadersMiddleware>(headers);
    }

    /// <summary>Adiciona apenas o middleware de proteção de cabeçalho <c>Host</c> (uso avulso).</summary>
    public static IApplicationBuilder UseAedisHostHeaderProtection(this IApplicationBuilder app) {
        var hostHeaders = app.ApplicationServices.GetRequiredService<IOptions<WebSecurityOptions>>().Value.HostHeaders;
        return app.UseMiddleware<HostHeaderProtectionMiddleware>(hostHeaders);
    }
}
