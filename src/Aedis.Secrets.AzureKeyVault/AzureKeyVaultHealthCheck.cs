using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Secrets.AzureKeyVault;

/// <summary>
///     Health check de <em>readiness</em> do Azure Key Vault por canário: lê o segredo
///     <see cref="AzureKeyVaultOptions.HealthCheckSecretName" /> (o valor é descartado, nunca logado) ou, na
///     ausência dele, um nome sentinela cujo HTTP 404 prova conectividade e autenticação. Não exige a
///     permissão de listar segredos — a identidade da aplicação pode ficar restrita aos segredos que usa.
/// </summary>
public sealed class AzureKeyVaultHealthCheck : IHealthCheck
{
    internal const string SentinelSecretName = "aedis-health-canary";

    private readonly SecretClient _client;
    private readonly ILogger<AzureKeyVaultHealthCheck> _logger;
    private readonly IOptions<AzureKeyVaultOptions> _options;

    /// <summary>Cria o health check sobre o <see cref="SecretClient" /> e as opções do provider.</summary>
    public AzureKeyVaultHealthCheck(SecretClient client, IOptions<AzureKeyVaultOptions> options,
        ILogger<AzureKeyVaultHealthCheck> logger) {
        _client = client;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        var configured = _options.Value.HealthCheckSecretName;
        var name = string.IsNullOrWhiteSpace(configured) ? SentinelSecretName : configured;

        try {
            await _client.GetSecretAsync(name, cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy("Azure Key Vault acessível.");
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && string.IsNullOrWhiteSpace(configured)) {
            return HealthCheckResult.Healthy("Azure Key Vault acessível.");
        }
        catch (RequestFailedException ex) when (ex.Status == 404) {
            _logger.LogError(ex, "Segredo canário do health check não existe no Azure Key Vault.");
            return HealthCheckResult.Unhealthy("Segredo canário do Azure Key Vault não encontrado.", ex);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Health check do Azure Key Vault falhou.");
            return HealthCheckResult.Unhealthy("Azure Key Vault indisponível.", ex);
        }
    }
}
