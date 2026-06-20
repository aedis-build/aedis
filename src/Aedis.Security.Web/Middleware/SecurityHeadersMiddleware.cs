using Aedis.Security.Web.Options;
using Microsoft.AspNetCore.Http;

namespace Aedis.Security.Web.Middleware;

/// <summary>
///     Adiciona os cabeçalhos de segurança de resposta configurados em <see cref="SecurityHeadersOptions" />
///     a cada requisição (CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Permissions-Policy
///     e as políticas Cross-Origin), antes de a resposta ser enviada. Quando desabilitado, é um no-op.
/// </summary>
/// <remarks>
///     Registrado por <c>UseAedisSecurityHeaders</c> / <c>UseAedisWebSecurity</c>. Os valores são fixos por
///     requisição (estáticos), então são gravados no início do middleware; um cabeçalho já presente é
///     sobrescrito pelo valor configurado para garantir a política.
/// </remarks>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    /// <summary>Cria o middleware com o próximo elo do pipeline e a política de cabeçalhos resolvida.</summary>
    public SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersOptions options) {
        _next = next;
        _options = options;
    }

    /// <summary>Aplica os cabeçalhos configurados à resposta e segue o pipeline.</summary>
    public Task InvokeAsync(HttpContext context) {
        if (_options.Enabled)
            ApplyHeaders(context.Response.Headers);

        return _next(context);
    }

    private void ApplyHeaders(IHeaderDictionary headers) {
        Set(headers, "Content-Security-Policy", _options.ContentSecurityPolicy);
        Set(headers, "X-Frame-Options", _options.FrameOptions);
        Set(headers, "Referrer-Policy", _options.ReferrerPolicy);
        Set(headers, "Permissions-Policy", _options.PermissionsPolicy);
        Set(headers, "Cross-Origin-Opener-Policy", _options.CrossOriginOpenerPolicy);
        Set(headers, "Cross-Origin-Resource-Policy", _options.CrossOriginResourcePolicy);

        if (_options.ContentTypeNoSniff)
            headers["X-Content-Type-Options"] = "nosniff";

        foreach (var (name, value) in _options.CustomHeaders)
            headers[name] = value;
    }

    private static void Set(IHeaderDictionary headers, string name, string? value) {
        if (!string.IsNullOrWhiteSpace(value))
            headers[name] = value;
    }
}
