using Aedis.Hosting.AspNetCore.Tenancy;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Pipeline HTTP da multi-tenancy do Aedis.
/// </summary>
public static class TenancyApplicationBuilderExtensions
{
    /// <summary>
    ///     Adiciona o <see cref="TenantResolutionMiddleware" />. Chame depois de <c>UseAuthentication</c>; exige
    ///     <c>AddAedisTenancy</c>. O <c>AedisApiHost</c> já faz os dois quando <c>EnableTenancy</c> é <c>true</c>.
    /// </summary>
    public static IApplicationBuilder UseAedisTenancy(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
