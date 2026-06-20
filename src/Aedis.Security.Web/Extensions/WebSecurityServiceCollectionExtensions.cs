using System.Net;
using System.Threading.RateLimiting;
using Aedis.Security.Web.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registra a camada de segurança HTTP do Aedis no contêiner de DI a partir da seção <c>Security</c>:
///     vincula as opções, configura os cabeçalhos encaminhados (forwarded headers), o HSTS e o rate limiter
///     global. O pipeline correspondente é ligado por <c>UseAedisWebSecurity</c>. Secure-by-default — basta
///     chamar <see cref="AddAedisWebSecurity" /> para ativar todos os controles.
/// </summary>
public static class WebSecurityServiceCollectionExtensions
{
    /// <summary>
    ///     Registra todos os controles de segurança HTTP do Aedis lendo a seção <c>Security</c> do
    ///     <paramref name="configuration" />. Idempotente quanto às opções; combine com <c>UseAedisWebSecurity</c>
    ///     no pipeline e com <c>ConfigureAedisKestrelHardening</c> no bootstrap do servidor.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configuration">Configuração de onde a seção <c>Security</c> é vinculada.</param>
    /// <returns>A própria <paramref name="services" />, para encadeamento.</returns>
    public static IServiceCollection AddAedisWebSecurity(this IServiceCollection services, IConfiguration configuration) {
        var section = configuration.GetSection(WebSecurityOptions.SectionName);

        services.AddOptions<WebSecurityOptions>().Bind(section).ValidateOnStart();

        var options = section.Get<WebSecurityOptions>() ?? new WebSecurityOptions();

        ConfigureForwardedHeaders(services, options.ForwardedHeaders);
        ConfigureHsts(services, options.Https);
        ConfigureRateLimiter(services, options.RateLimiting);

        return services;
    }

    private static void ConfigureForwardedHeaders(IServiceCollection services, ForwardedHeadersHardeningOptions forwarded) {
        if (!forwarded.Enabled)
            return;

        services.Configure<ForwardedHeadersOptions>(o => {
            o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.All;

            if (forwarded.TrustAllProxies) {
                o.KnownProxies.Clear();
                o.KnownNetworks.Clear();
                return;
            }

            foreach (var proxy in forwarded.KnownProxies)
                if (IPAddress.TryParse(proxy, out var address))
                    o.KnownProxies.Add(address);

            foreach (var network in forwarded.KnownNetworks) {
                var parts = network.Split('/');
                if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix) && int.TryParse(parts[1], out var prefixLength))
                    o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
            }
        });
    }

    private static void ConfigureHsts(IServiceCollection services, HttpsOptions https) {
        if (!https.EnableHsts)
            return;

        services.AddHsts(o => {
            o.MaxAge = TimeSpan.FromDays(https.HstsMaxAgeDays);
            o.IncludeSubDomains = https.HstsIncludeSubDomains;
            o.Preload = https.HstsPreload;
        });
    }

    private static void ConfigureRateLimiter(IServiceCollection services, RateLimitingOptions rateLimiting) {
        if (!rateLimiting.Enabled)
            return;

        services.AddRateLimiter(o => {
            o.RejectionStatusCode = rateLimiting.RejectionStatusCode;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions {
                        PermitLimit = rateLimiting.PermitLimit,
                        Window = rateLimiting.Window,
                        QueueLimit = rateLimiting.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));
        });
    }

    private static string ResolvePartitionKey(HttpContext context) {
        if (context.User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(context.User.Identity.Name))
            return $"user:{context.User.Identity.Name}";

        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
