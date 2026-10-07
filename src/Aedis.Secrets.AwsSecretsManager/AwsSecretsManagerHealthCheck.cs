using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Secrets.AwsSecretsManager;

/// <summary>
///     Health check de <em>readiness</em> do AWS Secrets Manager por canário: executa <c>DescribeSecret</c>
///     (metadados, nunca o valor) sobre <see cref="AwsSecretsManagerOptions.HealthCheckSecretName" /> ou, na
///     ausência dele, sobre um nome sentinela cujo "não encontrado" prova conectividade e autenticação. Não
///     exige <c>ListSecrets</c> — a role da aplicação pode ficar restrita aos segredos que ela usa.
/// </summary>
public sealed class AwsSecretsManagerHealthCheck : IHealthCheck
{
    internal const string SentinelSecretName = "aedis/health/canary";

    private readonly IAmazonSecretsManager _client;
    private readonly ILogger<AwsSecretsManagerHealthCheck> _logger;
    private readonly IOptions<AwsSecretsManagerOptions> _options;

    /// <summary>Cria o health check sobre o cliente do Secrets Manager e as opções do provider.</summary>
    public AwsSecretsManagerHealthCheck(IAmazonSecretsManager client, IOptions<AwsSecretsManagerOptions> options,
        ILogger<AwsSecretsManagerHealthCheck> logger) {
        _client = client;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        var configured = _options.Value.HealthCheckSecretName;
        var secretId = string.IsNullOrWhiteSpace(configured) ? SentinelSecretName : configured;

        try {
            await _client.DescribeSecretAsync(new DescribeSecretRequest { SecretId = secretId }, cancellationToken);
            return HealthCheckResult.Healthy("AWS Secrets Manager acessível.");
        }
        catch (ResourceNotFoundException) when (string.IsNullOrWhiteSpace(configured)) {
            return HealthCheckResult.Healthy("AWS Secrets Manager acessível.");
        }
        catch (ResourceNotFoundException ex) {
            _logger.LogError(ex, "Segredo canário do health check não existe no AWS Secrets Manager.");
            return HealthCheckResult.Unhealthy("Segredo canário do AWS Secrets Manager não encontrado.", ex);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Health check do AWS Secrets Manager falhou.");
            return HealthCheckResult.Unhealthy("AWS Secrets Manager indisponível.", ex);
        }
    }
}
