using Aedis.Security;
using Aedis.Security.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI dos serviços de segurança agnósticos do Aedis.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o <see cref="IAuditContext" /> derivado do usuário logado
    ///     (<see cref="CurrentUserAuditContext" />, scoped). Com um <see cref="ICurrentUser" /> registrado,
    ///     o provider de persistência passa a carimbar automaticamente o usuário autenticado nas colunas de
    ///     auditoria; sem usuário logado, cai no ator default do provider. A mesma instância é exposta como
    ///     <see cref="IAuditContext" /> e como <see cref="CurrentUserAuditContext" /> (para definir o
    ///     <c>Reason</c> por operação).
    /// </summary>
    public static IServiceCollection AddAedisAuditContext(this IServiceCollection services) {
        services.TryAddScoped<CurrentUserAuditContext>();
        services.TryAddScoped<IAuditContext>(sp => sp.GetRequiredService<CurrentUserAuditContext>());
        return services;
    }
}
