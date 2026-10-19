using System.Security.Claims;
using System.Text.Json;
using Aedis.Tenancy.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aedis.Security.Keycloak;

/// <summary>
///     <see cref="ITenantResolver" /> a partir das Organizations do Keycloak: lê a claim
///     <see cref="KeycloakAuthOptions.OrganizationClaimType" /> do token da requisição. Aceita as duas formas
///     que o Keycloak emite — lista de aliases (<c>["acme"]</c>) ou objeto por alias
///     (<c>{"acme":{"id":"…"}}</c>, com "Add organization id") — e devolve o alias ou, com
///     <see cref="KeycloakAuthOptions.TenantFromOrganizationId" />, o id.
/// </summary>
/// <remarks>
///     Fail-closed: membro de nenhuma ou de mais de uma organização → <c>null</c> (sem tenant). Para um usuário
///     com várias organizações, o cliente pede o token com o escopo <c>organization:&lt;alias&gt;</c>, e o
///     Keycloak emite só a escolhida. Com <see cref="KeycloakAuthOptions.TenantFromOrganizationId" /> e claim
///     sem id, também <c>null</c>.
/// </remarks>
public sealed class KeycloakOrganizationTenantResolver(IHttpContextAccessor accessor,
    IOptions<KeycloakAuthOptions> options) : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(Resolve(accessor.HttpContext?.User));

    private string? Resolve(ClaimsPrincipal? principal) {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var claims = principal.FindAll(options.Value.OrganizationClaimType).ToArray();
        if (claims.Length != 1)
            return null;

        var value = claims[0].Value.Trim();
        return value.StartsWith('{') ? FromObject(value) : FromAlias(value);
    }

    private string? FromAlias(string alias) =>
        options.Value.TenantFromOrganizationId || alias.Length == 0 || alias.StartsWith('[') ? null : alias;

    private string? FromObject(string json) {
        try {
            using var document = JsonDocument.Parse(json);
            var organizations = document.RootElement.EnumerateObject().ToArray();
            if (organizations.Length != 1)
                return null;

            var organization = organizations[0];
            if (!options.Value.TenantFromOrganizationId)
                return organization.Name.Length == 0 ? null : organization.Name;

            return organization.Value.ValueKind == JsonValueKind.Object
                   && organization.Value.TryGetProperty("id", out var id)
                   && id.ValueKind == JsonValueKind.String
                   && id.GetString() is { Length: > 0 } tenantId
                ? tenantId
                : null;
        }
        catch (JsonException) {
            return null;
        }
    }
}
