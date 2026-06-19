using Aedis.Scheduling.Hangfire;
using global::Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Mapeamento do dashboard Hangfire do Aedis.
/// </summary>
public static class HangfireApplicationBuilderExtensions
{
    /// <summary>
    ///     Mapeia o dashboard em <c>/{DashboardPath}</c> quando habilitado. A autorização é permissiva por
    ///     padrão — restrinja o acesso no ingress/gateway.
    /// </summary>
    public static IApplicationBuilder UseAedisHangfireDashboard(this IApplicationBuilder app) {
        var options = app.ApplicationServices.GetRequiredService<IOptions<HangfireOptions>>().Value;
        if (!options.EnableDashboard)
            return app;

        return app.UseHangfireDashboard($"/{options.DashboardPath.TrimStart('/')}", new DashboardOptions {
            Authorization = [new AllowAllDashboardAuthorizationFilter()]
        });
    }
}
