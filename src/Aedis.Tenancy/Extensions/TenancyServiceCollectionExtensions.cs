using Aedis.Tenancy;
using Aedis.Tenancy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI da multi-tenancy do Aedis.
/// </summary>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o tenant ambiente (<see cref="AmbientTenantContext" />, singleton) como
    ///     <see cref="ITenantContext" /> e <see cref="ICurrentTenant" />, e o resolvedor por claim
    ///     (<see cref="ClaimTenantResolver" />, scoped) se nenhum outro <see cref="ITenantResolver" /> foi
    ///     registrado. Lê as opções da seção <c>Tenancy</c>. Idempotente.
    /// </summary>
    /// <remarks>
    ///     Para trocar a origem do tenant, registre o seu <see cref="ITenantResolver" /> (ex.: consulta ao
    ///     cadastro) ou use <c>AddAedisKeycloakOrganizationTenancy()</c>; ambos podem vir antes ou depois desta chamada.
    /// </remarks>
    public static IServiceCollection AddAedisTenancy(this IServiceCollection services, IConfiguration configuration) {
        services.AddOptions<TenancyOptions>()
            .Bind(configuration.GetSection(TenancyOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClaimType), "Tenancy:ClaimType não pode ser vazio.")
            .ValidateOnStart();

        services.TryAddSingleton<AmbientTenantContext>();
        services.TryAddSingleton<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.TryAddSingleton<ICurrentTenant>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.TryAddScoped<ITenantResolver, ClaimTenantResolver>();
        return services;
    }
}
