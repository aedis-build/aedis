using Hangfire.Dashboard;

namespace Aedis.Scheduling.Hangfire;

/// <summary>
///     Autorização permissiva do dashboard Hangfire — todos os requests passam. O acesso externo deve ser
///     controlado pelo ingress/gateway. Usado por padrão quando o dashboard está habilitado.
/// </summary>
internal sealed class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
