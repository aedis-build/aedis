using Microsoft.AspNetCore.Http;

namespace Aedis.Hosting.AspNetCore.Hateoas;

/// <summary>
///     Utilitários sobre o <see cref="HttpContext" /> para a construção de links HATEOAS, isolando a montagem da
///     URL base da requisição em um único ponto.
/// </summary>
public static class HateoasHttpContextExtensions {
    /// <summary>
    ///     Retorna a URL base da requisição atual (<c>esquema://host{pathBase}</c>), usada como prefixo dos links
    ///     absolutos. Respeita <c>PathBase</c> quando a aplicação roda sob um caminho (por exemplo, atrás de um
    ///     gateway).
    /// </summary>
    /// <param name="httpContext">Contexto da requisição atual.</param>
    public static string GetBaseUrl(this HttpContext httpContext) {
        ArgumentNullException.ThrowIfNull(httpContext);
        var request = httpContext.Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}";
    }
}
