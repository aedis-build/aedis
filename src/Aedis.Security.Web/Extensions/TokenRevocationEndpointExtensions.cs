using Aedis.Security.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Expõe endpoints administrativos de revogação de token (para operador/SOC), protegidos por uma policy
///     de autorização informada pela aplicação. Requer um <see cref="ITokenRevocation" /> registrado
///     (<c>AddAedisTokenDenylist</c>/<c>AddAedisTokenAbuseGuard</c>). É opt-in — a aplicação decide se mapeia.
/// </summary>
public static class TokenRevocationEndpointExtensions
{
    /// <summary>
    ///     Mapeia <c>POST {prefix}/revoke/{tokenId}</c> (revoga um token por <c>jti</c>) e
    ///     <c>POST {prefix}/revoke-user/{subject}</c> (revoga todas as sessões de um usuário), ambos exigindo
    ///     a policy <paramref name="authorizationPolicy" />.
    /// </summary>
    /// <param name="endpoints">Builder de rotas da aplicação.</param>
    /// <param name="authorizationPolicy">Policy de autorização que protege os endpoints (ex.: <c>"admin"</c>).</param>
    /// <param name="prefix">Prefixo das rotas. Default <c>/security/tokens</c>.</param>
    public static IEndpointRouteBuilder MapAedisTokenRevocation(
        this IEndpointRouteBuilder endpoints,
        string authorizationPolicy,
        string prefix = "/security/tokens") {
        var group = endpoints.MapGroup(prefix).RequireAuthorization(authorizationPolicy);

        group.MapPost("/revoke/{tokenId}", async (string tokenId, ITokenRevocation revocation, CancellationToken cancellationToken) => {
            await revocation.RevokeTokenAsync(tokenId, cancellationToken: cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/revoke-user/{subject}", async (string subject, ITokenRevocation revocation, CancellationToken cancellationToken) => {
            await revocation.RevokeUserAsync(subject, cancellationToken);
            return Results.NoContent();
        });

        return endpoints;
    }
}
