using Aedis.Security.Web.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Aplica o endurecimento do servidor Kestrel a partir da subseção <c>Security:Kestrel</c>: remove o
///     cabeçalho <c>Server</c>, bloqueia I/O síncrono e impõe limites de corpo, cabeçalhos e timeouts.
///     Chame no bootstrap, sobre o <see cref="WebApplicationBuilder" />, antes de <c>builder.Build()</c>.
/// </summary>
public static class KestrelHardeningExtensions
{
    /// <summary>
    ///     Configura o Kestrel com os limites de segurança do Aedis. Quando a opção está desligada
    ///     (<c>Security:Kestrel:Enabled = false</c>), não altera nada.
    /// </summary>
    /// <param name="builder">Builder da aplicação web em construção.</param>
    /// <returns>O próprio <paramref name="builder" />, para encadeamento.</returns>
    public static WebApplicationBuilder ConfigureAedisKestrelHardening(this WebApplicationBuilder builder) {
        var options = builder.Configuration.GetSection(WebSecurityOptions.SectionName).Get<WebSecurityOptions>()?.Kestrel
                      ?? new KestrelHardeningOptions();

        if (!options.Enabled)
            return builder;

        builder.WebHost.ConfigureKestrel(kestrel => {
            kestrel.AddServerHeader = !options.RemoveServerHeader;
            kestrel.AllowSynchronousIO = !options.DisallowSynchronousIO;
            kestrel.Limits.MaxRequestBodySize = options.MaxRequestBodySizeBytes;
            kestrel.Limits.KeepAliveTimeout = options.KeepAliveTimeout;
            kestrel.Limits.RequestHeadersTimeout = options.RequestHeadersTimeout;
            kestrel.Limits.MaxRequestHeaderCount = options.MaxRequestHeaderCount;
            kestrel.Limits.MaxRequestHeadersTotalSize = options.MaxRequestHeadersTotalSize;
        });

        return builder;
    }
}
