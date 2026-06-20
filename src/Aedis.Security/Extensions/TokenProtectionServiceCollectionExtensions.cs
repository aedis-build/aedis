using Aedis.Security.Abstractions;
using Aedis.Security.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registra a revogação de token (<see cref="ITokenDenylist" />) e o guard de abuso por token/usuário
///     atual (<see cref="ITokenAbuseGuard" />), ambos sobre o <c>ICache</c>. Requerem um <c>ICache</c>
///     registrado; o guard de abuso requer também um <see cref="ICurrentUser" /> (do provider de autenticação).
/// </summary>
public static class TokenProtectionServiceCollectionExtensions
{
    /// <summary>Registra a denylist de tokens (<see cref="CacheTokenDenylist" />). Imposta na validação do JWT do provider.</summary>
    public static IServiceCollection AddAedisTokenDenylist(this IServiceCollection services) {
        services.TryAddSingleton<ITokenDenylist, CacheTokenDenylist>();
        return services;
    }

    /// <summary>
    ///     Registra a proteção completa do cenário de token vazado: o <see cref="IBruteForceGuard" /> (com os
    ///     3 níveis de bloqueio), a <see cref="ITokenDenylist" /> e o <see cref="ITokenAbuseGuard" /> (scoped)
    ///     que liga ambos ao token/usuário atual.
    /// </summary>
    public static IServiceCollection AddAedisTokenAbuseGuard(this IServiceCollection services, IConfiguration configuration) {
        services.AddAedisBruteForceGuard(configuration);
        services.AddAedisTokenDenylist();
        services.TryAddScoped<ITokenAbuseGuard, TokenAbuseGuard>();
        return services;
    }
}
