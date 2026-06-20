using System.Net;
using Aedis.Security.Web.Options;
using Microsoft.AspNetCore.Http;

namespace Aedis.Security.Web.Middleware;

/// <summary>
///     Recusa com <c>400 Invalid Host header</c> as requisições cujo cabeçalho <c>Host</c> não esteja na
///     lista de permitidos de <see cref="HostHeaderProtectionOptions" />, defendendo contra host-header
///     injection e cache poisoning. Aceita por padrão acessos internos legítimos (localhost, IP direto,
///     DNS de cluster) e isenta os prefixos de probe (saúde/métricas).
/// </summary>
public sealed class HostHeaderProtectionMiddleware
{
    private const string ClusterDnsSuffix = ".svc.cluster.local";

    private readonly RequestDelegate _next;
    private readonly HostHeaderProtectionOptions _options;

    /// <summary>Cria o middleware com o próximo elo do pipeline e a política de Host resolvida.</summary>
    public HostHeaderProtectionMiddleware(RequestDelegate next, HostHeaderProtectionOptions options) {
        _next = next;
        _options = options;
    }

    /// <summary>Valida o Host da requisição e, se permitido (ou isento), segue o pipeline.</summary>
    public Task InvokeAsync(HttpContext context) {
        if (!_options.Enabled || IsBypassed(context.Request.Path) || IsHostAllowed(context.Request.Host.Host))
            return _next(context);

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsync("Invalid Host header");
    }

    private bool IsBypassed(PathString path) {
        foreach (var prefix in _options.BypassPathPrefixes)
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private bool IsHostAllowed(string host) {
        if (string.IsNullOrEmpty(host))
            return false;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (_options.AllowDirectIpAccess && IPAddress.TryParse(host, out _))
            return true;

        if (host.EndsWith(ClusterDnsSuffix, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var allowed in _options.AllowedHosts)
            if (Matches(host, allowed))
                return true;

        return false;
    }

    private static bool Matches(string host, string pattern) {
        if (pattern.StartsWith("*.", StringComparison.Ordinal)) {
            var suffix = pattern[1..];
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && host.Length > suffix.Length;
        }

        return string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
