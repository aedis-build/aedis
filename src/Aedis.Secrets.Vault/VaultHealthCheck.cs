using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using VaultSharp;

namespace Aedis.Secrets.Vault;

/// <summary>
///     Health check de <em>readiness</em> do HashiCorp Vault: consulta o status de saúde e exige que o
///     servidor esteja inicializado e <strong>não selado</strong> (unsealed) para responder requisições.
/// </summary>
public sealed class VaultHealthCheck : IHealthCheck
{
    private readonly IVaultClient _client;
    private readonly ILogger<VaultHealthCheck> _logger;

    /// <summary>Cria o health check sobre o <see cref="IVaultClient" />.</summary>
    public VaultHealthCheck(IVaultClient client, ILogger<VaultHealthCheck> logger) {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        try {
            var status = await _client.V1.System.GetHealthStatusAsync();
            return status is { Initialized: true, Sealed: false }
                ? HealthCheckResult.Healthy("Vault inicializado e não selado.")
                : HealthCheckResult.Unhealthy("Vault não está pronto (não inicializado ou selado).");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Health check do Vault falhou.");
            return HealthCheckResult.Unhealthy("Vault indisponível.", ex);
        }
    }
}
