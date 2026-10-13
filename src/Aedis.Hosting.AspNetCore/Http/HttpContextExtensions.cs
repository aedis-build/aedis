using Aedis.Hosting.AspNetCore.Filters;
using Microsoft.AspNetCore.Http;

namespace Aedis.Hosting.AspNetCore.Http;

/// <summary>Auxiliares de <see cref="HttpContext" /> usados pela hipermídia e pelos exports.</summary>
public static class HttpContextExtensions
{
    /// <summary>URL base da requisição (<c>scheme://host/pathBase</c>), para montar links absolutos.</summary>
    public static string GetBaseUrl(this HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}";
    }

    /// <summary>Verdadeiro quando a requisição usa o método <c>QUERY</c>.</summary>
    public static bool IsQueryMethod(this HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);
        return string.Equals(context.Request.Method, HttpQueryAttribute.Method, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Request de consulta da action corrente, capturado pelo <see cref="ActionRequestCaptureFilter" />; a
    ///     hipermídia o ecoa nos <c>_templates</c> sem o controller precisar repassá-lo. <c>null</c> quando não
    ///     houve candidato único.
    /// </summary>
    public static object? GetCapturedActionRequest(this HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(ActionRequestCaptureFilter.ItemKey, out var request) ? request : null;
    }
}
