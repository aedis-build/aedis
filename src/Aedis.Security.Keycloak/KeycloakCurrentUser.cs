using System.Security.Claims;
using Aedis.Security.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aedis.Security.Keycloak;

/// <summary>
///     <see cref="ICurrentUser" /> a partir do token JWT do Keycloak presente na requisição atual
///     (<see cref="IHttpContextAccessor" /> → <see cref="ClaimsPrincipal" />). Lê o usuário pelos claims
///     configurados em <see cref="KeycloakAuthOptions" /> (<c>sub</c>/<c>name</c>/<c>roles</c>). É o elo que
///     leva o usuário logado até o carimbo de auditoria (via <c>CurrentUserAuditContext</c>). Scoped.
/// </summary>
public sealed class KeycloakCurrentUser(IHttpContextAccessor accessor, IOptions<KeycloakAuthOptions> options)
    : ICurrentUser
{
    private readonly KeycloakAuthOptions _options = options.Value;

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public string? Id => Find(_options.IdClaimType) ?? Find(ClaimTypes.NameIdentifier);

    /// <inheritdoc />
    public string? Name => Find(_options.NameClaimType) ?? Principal?.Identity?.Name;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(_options.RoleClaimType).Select(c => c.Value).ToArray() ?? [];

    /// <inheritdoc />
    public string? FindClaim(string type) => Find(type);

    private string? Find(string type) => Principal?.FindFirst(type)?.Value;
}
