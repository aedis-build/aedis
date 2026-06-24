using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Aedis.Secrets.AwsSecretsManager;

/// <summary>
///     Health check de <em>readiness</em> do AWS Secrets Manager: lista 1 segredo para provar conectividade
///     e permissão. Não lê nenhum valor de segredo (evita expor PII no probe).
/// </summary>
public sealed class AwsSecretsManagerHealthCheck : IHealthCheck
{
    private readonly IAmazonSecretsManager _client;
    private readonly ILogger<AwsSecretsManagerHealthCheck> _logger;

    /// <summary>Cria o health check sobre o cliente do Secrets Manager.</summary>
    public AwsSecretsManagerHealthCheck(IAmazonSecretsManager client, ILogger<AwsSecretsManagerHealthCheck> logger) {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        try {
            await _client.ListSecretsAsync(new ListSecretsRequest { MaxResults = 1 }, cancellationToken);
            return HealthCheckResult.Healthy("AWS Secrets Manager acessível.");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Health check do AWS Secrets Manager falhou.");
            return HealthCheckResult.Unhealthy("AWS Secrets Manager indisponível.", ex);
        }
    }
}
