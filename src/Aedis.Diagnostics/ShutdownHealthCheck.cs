using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aedis.Diagnostics;

/// <summary>
///     Health check de <em>readiness</em> que falha quando o desligamento gracioso é iniciado, fazendo o
///     load balancer remover o pod antes de parar de aceitar requisições (drena o tráfego com segurança).
/// </summary>
public sealed class ShutdownHealthCheck : IHealthCheck
{
    private volatile bool _isShuttingDown;

    /// <summary>Indica se o desligamento já foi sinalizado via <see cref="MarkAsShuttingDown" />.</summary>
    public bool IsShuttingDown => _isShuttingDown;

    /// <summary>
    ///     Reporta Healthy enquanto a aplicação opera normalmente e Unhealthy assim que o desligamento é
    ///     iniciado, fazendo o <c>/health/ready</c> falhar para que o orquestrador pare de rotear tráfego.
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        return Task.FromResult(_isShuttingDown
            ? HealthCheckResult.Unhealthy("A aplicação está sendo desligada.")
            : HealthCheckResult.Healthy());
    }

    /// <summary>Marca a aplicação como em desligamento — o check passa a reportar Unhealthy.</summary>
    public void MarkAsShuttingDown() {
        _isShuttingDown = true;
    }
}
