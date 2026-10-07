using System.Security.Cryptography;
using System.Text;
using Aedis.Signing.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Rota que publica o JWKS da aplicação. Quem mapeia é a aplicação, no path que o gateway vai expor; um
///     worker simplesmente não mapeia. Por padrão a rota é anônima; com <c>allowAnonymous: false</c> exige
///     apenas um chamador autenticado (<c>RequireAuthorization()</c>, sem policy) — o metadata
///     <c>AllowAnonymous</c> venceria qualquer <c>RequireAuthorization</c> aplicado depois, por isso a escolha é
///     feita aqui.
/// </summary>
public static class SigningJwksEndpointExtensions
{
    /// <summary>Nome da rota.</summary>
    public const string EndpointName = "AedisSigningJwks";

    /// <summary>
    ///     Mapeia <c>GET {path}</c> (padrão: <c>Signing:Jwks:Path</c>) com <c>Cache-Control</c> e ETag. Responde
    ///     503 enquanto a chave não está pronta e 404 com a assinatura desligada.
    /// </summary>
    /// <param name="endpoints">Builder de rotas da aplicação.</param>
    /// <param name="path">Path da rota; <c>null</c> usa <c>Signing:Jwks:Path</c>.</param>
    /// <param name="allowAnonymous"><c>true</c> (padrão): rota anônima; <c>false</c>: exige chamador autenticado.</param>
    public static IEndpointConventionBuilder MapAedisSigningJwks(this IEndpointRouteBuilder endpoints, string? path = null,
        bool allowAnonymous = true) {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<IOptions<SigningOptions>>()?.Value
                      ?? throw new InvalidOperationException("Assinatura não registrada. Chame services.AddAedisSigning(configuration) antes.");
        var route = string.IsNullOrWhiteSpace(path) ? options.Jwks.Path : path;

        var endpoint = endpoints.MapGet(route, (HttpContext context, IJwksProvider jwks) => {
                if (!options.Enabled)
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Assinatura desligada");

                if (!jwks.TryGet(out var document) || document is null)
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Chave de assinatura não pronta", detail: "A chave de assinatura ainda não foi resolvida; tente novamente em instantes.");

                var json = document.ToJson();
                var etag = $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32]}\"";

                context.Response.Headers.CacheControl = $"public, max-age={options.Jwks.CacheSeconds}";
                context.Response.Headers.ETag = etag;

                if (context.Request.Headers.IfNoneMatch.Any(v => string.Equals(v, etag, StringComparison.Ordinal)))
                    return Results.StatusCode(StatusCodes.Status304NotModified);

                return Results.Text(json, "application/json", Encoding.UTF8);
            })
            .WithName(EndpointName)
            .WithTags("Well-Known");

        return allowAnonymous ? endpoint.AllowAnonymous() : endpoint.RequireAuthorization();
    }
}
