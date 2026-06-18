namespace Aedis.Security.Keycloak;

/// <summary>
///     Opções de autenticação Keycloak (JWT Bearer). Lidas da seção <c>Auth</c> da configuração.
/// </summary>
public sealed class KeycloakAuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>URL do realm (issuer), ex.: <c>https://auth.exemplo.com/realms/dev</c>. Valida <c>iss</c> e descobre o JWKS.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Audience esperada (<c>aud</c>). Vazio desabilita a validação de audience.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Exige HTTPS no Authority. Padrão true (use false só em dev local).</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Claim do identificador do usuário. Padrão <c>sub</c>.</summary>
    public string IdClaimType { get; set; } = "sub";

    /// <summary>Claim do nome do usuário. Padrão <c>name</c>.</summary>
    public string NameClaimType { get; set; } = "name";

    /// <summary>Claim das roles (array flat no token). Padrão <c>roles</c>.</summary>
    public string RoleClaimType { get; set; } = "roles";
}
