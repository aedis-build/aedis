using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Aedis.Secrets.AzureKeyVault;

/// <summary>
///     Health check de <em>readiness</em> do Azure Key Vault: lista propriedades de segredos (primeira
///     página) para provar conectividade e permissão, sem ler nenhum valor (evita expor PII no probe).
/// </summary>
public sealed class AzureKeyVaultHealthCheck : IHealthCheck
{
    private readonly SecretClient _client;
    private readonly ILogger<AzureKeyVaultHealthCheck> _logger;

    /// <summary>Cria o health check sobre o <see cref="SecretClient" />.</summary>
    public AzureKeyVaultHealthCheck(SecretClient client, ILogger<AzureKeyVaultHealthCheck> logger) {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        try {
            await foreach (var _ in _client.GetPropertiesOfSecretsAsync(cancellationToken)) {
                break;
            }

            return HealthCheckResult.Healthy("Azure Key Vault acessível.");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Health check do Azure Key Vault falhou.");
            return HealthCheckResult.Unhealthy("Azure Key Vault indisponível.", ex);
        }
    }
}
