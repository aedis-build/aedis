namespace Aedis.Security.Web.Options;

/// <summary>
///     Raiz de configuração da camada de segurança HTTP do Aedis, vinculada à seção <c>Security</c> do
///     <c>IConfiguration</c>. Agrega os controles secure-by-default (todos ligados): cabeçalhos de segurança,
///     TLS/HSTS, rate limiting, proteção de Host, endurecimento do Kestrel e cabeçalhos encaminhados. Cada
///     subseção mapeia para a classe de opções correspondente e pode ser ajustada ou desligada
///     individualmente sem afetar as demais.
/// </summary>
/// <example>
///     <code>
///     "Security": {
///       "Https": { "EnableHttpsRedirection": false },
///       "RateLimiting": { "PermitLimit": 300 }
///     }
///     </code>
/// </example>
public sealed class WebSecurityOptions
{
    /// <summary>Nome da seção de configuração que vincula estas opções.</summary>
    public const string SectionName = "Security";

    /// <summary>Cabeçalhos de segurança de resposta (CSP, X-Frame-Options, nosniff etc.).</summary>
    public SecurityHeadersOptions Headers { get; set; } = new();

    /// <summary>Postura de TLS: HTTPS-redirect e HSTS.</summary>
    public HttpsOptions Https { get; set; } = new();

    /// <summary>Rate limiting global por cliente.</summary>
    public RateLimitingOptions RateLimiting { get; set; } = new();

    /// <summary>Proteção de cabeçalho <c>Host</c>.</summary>
    public HostHeaderProtectionOptions HostHeaders { get; set; } = new();

    /// <summary>Endurecimento do servidor Kestrel (limites, timeouts, remoção do header Server).</summary>
    public KestrelHardeningOptions Kestrel { get; set; } = new();

    /// <summary>Interpretação de cabeçalhos <c>X-Forwarded-*</c> de proxy/ingress.</summary>
    public ForwardedHeadersHardeningOptions ForwardedHeaders { get; set; } = new();
}
