using Aedis.Security.Abstractions;
using Aedis.Tenancy.Abstractions;
using Microsoft.Extensions.Options;

namespace Aedis.Tenancy;

/// <summary>
///     <see cref="ITenantResolver" /> padrão: lê o tenant da claim <see cref="TenancyOptions.ClaimType" /> do
///     usuário autenticado (<see cref="ICurrentUser.FindClaim" />). Anônimo, sem <see cref="ICurrentUser" />
///     registrado (ex.: worker) ou claim vazia → <c>null</c>.
/// </summary>
public sealed class ClaimTenantResolver(IOptions<TenancyOptions> options, ICurrentUser? currentUser = null)
    : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(CancellationToken ct = default) {
        var tenant = currentUser is { IsAuthenticated: true } ? currentUser.FindClaim(options.Value.ClaimType) : null;
        return ValueTask.FromResult(string.IsNullOrWhiteSpace(tenant) ? null : tenant);
    }
}
