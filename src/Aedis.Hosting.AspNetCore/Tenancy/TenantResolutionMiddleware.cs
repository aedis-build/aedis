using Aedis.Tenancy.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Aedis.Hosting.AspNetCore.Tenancy;

/// <summary>
///     Resolve o tenant da requisição com o <see cref="ITenantResolver" /> registrado e abre um escopo de
///     tenant (<see cref="ITenantContext.BeginScope" />) para o restante do pipeline. Sem tenant resolvido, a
///     requisição segue sem tenant — endpoints que acessam dado escopado falham fechado
///     (<see cref="TenantRequiredException" /> → 403). Deve rodar depois da autenticação.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    /// <summary>Resolve o tenant e executa o próximo middleware dentro do escopo dele.</summary>
    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, ITenantContext tenantContext) {
        var tenantId = await resolver.ResolveAsync(context.RequestAborted);
        if (tenantId is null) {
            await next(context);
            return;
        }

        using (tenantContext.BeginScope(tenantId))
            await next(context);
    }
}
