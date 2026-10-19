namespace Aedis.Tenancy;

/// <summary>
///     Opções de multi-tenancy. Lidas da seção <c>Tenancy</c> da configuração.
/// </summary>
public sealed class TenancyOptions
{
    /// <summary>Nome da seção de configuração de onde estas opções são lidas (<c>"Tenancy"</c>).</summary>
    public const string SectionName = "Tenancy";

    /// <summary>
    ///     Claim do token com o identificador do tenant, lida pelo <see cref="ClaimTenantResolver" />. Padrão
    ///     <c>tenant_id</c>.
    /// </summary>
    public string ClaimType { get; set; } = "tenant_id";
}
