using System.Security.Claims;
using Aedis.Observability.Serilog;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace Aedis.Hosting.AspNetCore.RequestLogging;

/// <summary>
///     Enriquecimento do access-log de requisições, <strong>seguro por construção</strong>. Adiciona apenas
///     campos úteis e não sensíveis (host, esquema, protocolo, user-agent, tipo de conteúdo da resposta e o
///     <c>UserId</c> derivado do <c>sub</c> autenticado) e a <c>QueryString</c> já ofuscada por
///     <see cref="QueryStringRedactor" /> — nunca o header <c>Authorization</c>. Como o evento final passa pelo
///     <see cref="RedactionEnricher" />, há ainda uma segunda camada de defesa por nome.
/// </summary>
public static class AedisRequestLogging {
    /// <summary>
    ///     Preenche o contexto de diagnóstico do access-log com os campos seguros da requisição atual.
    /// </summary>
    /// <param name="diagnosticContext">Contexto de diagnóstico do Serilog para a requisição.</param>
    /// <param name="httpContext">Contexto HTTP da requisição.</param>
    /// <param name="redaction">Opções de ofuscação aplicadas à query string.</param>
    public static void Enrich(IDiagnosticContext diagnosticContext, HttpContext httpContext, RedactionOptions redaction) {
        var request = httpContext.Request;

        diagnosticContext.Set("Host", request.Host.Value);
        diagnosticContext.Set("Scheme", request.Scheme);
        diagnosticContext.Set("Protocol", request.Protocol);

        if (request.QueryString.HasValue) {
            diagnosticContext.Set("QueryString", QueryStringRedactor.Redact(request.QueryString.Value, redaction));
        }

        var userAgent = request.Headers.UserAgent.ToString();
        if (!string.IsNullOrEmpty(userAgent)) {
            diagnosticContext.Set("UserAgent", userAgent);
        }

        if (!string.IsNullOrEmpty(httpContext.Response.ContentType)) {
            diagnosticContext.Set("ResponseContentType", httpContext.Response.ContentType);
        }

        var userId = httpContext.User?.FindFirst("sub")?.Value
                     ?? httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId)) {
            diagnosticContext.Set("UserId", userId);
        }
    }
}
