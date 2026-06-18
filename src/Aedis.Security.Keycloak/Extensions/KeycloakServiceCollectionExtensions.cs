using Aedis.Security.Abstractions;
using Aedis.Security.Keycloak;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI da autenticação Keycloak (JWT Bearer) do Aedis.
/// </summary>
public static class KeycloakServiceCollectionExtensions
{
    /// <summary>
    ///     Configura o JWT Bearer validando o token do Keycloak (issuer/audience/lifetime) e registra o
    ///     <see cref="ICurrentUser" /> derivado do token (<see cref="KeycloakCurrentUser" />, scoped),
    ///     além do <c>IHttpContextAccessor</c>. Lê as opções da seção <c>Auth</c>. Preserva os
    ///     nomes de claim do Keycloak (<c>sub</c>/<c>name</c>/<c>roles</c>). Combine com
    ///     <c>AddAedisAuditContext()</c> para o usuário logado fluir até o carimbo de auditoria.
    /// </summary>
    public static AuthenticationBuilder AddAedisKeycloakAuth(this IServiceCollection services,
        IConfiguration configuration) {
        services.AddOptions<KeycloakAuthOptions>()
            .Bind(configuration.GetSection(KeycloakAuthOptions.SectionName))
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.TryAddScoped<ICurrentUser, KeycloakCurrentUser>();

        var options = configuration.GetSection(KeycloakAuthOptions.SectionName).Get<KeycloakAuthOptions>()
                      ?? new KeycloakAuthOptions();

        return services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt => {
                jwt.Authority = options.Authority;
                jwt.Audience = options.Audience;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwt.MapInboundClaims = false; // preserva sub/name/roles do Keycloak (sem remapear para ClaimTypes.*)
                jwt.TokenValidationParameters = new TokenValidationParameters {
                    ValidateIssuer = true,
                    ValidIssuer = options.Authority,
                    ValidateAudience = !string.IsNullOrWhiteSpace(options.Audience),
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    NameClaimType = options.NameClaimType,
                    RoleClaimType = options.RoleClaimType
                };
            });
    }
}
