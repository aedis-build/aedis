using Aedis.Secrets.Abstractions;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aedis.Secrets.AzureKeyVault;

/// <summary>
///     Provider de segredos sobre o Azure Key Vault. Lê o valor e expõe metadados (versão, <c>UpdatedOn</c>
///     como rotação). Segredo inexistente (HTTP 404) devolve <c>null</c>; falhas transitórias do Azure
///     sobem para o chamador. Normalmente é envolvido pelo <c>CachingSecretsProvider</c> via DI.
/// </summary>
public sealed class AzureKeyVaultProvider : ISecretsProvider
{
    private readonly SecretClient _client;
    private readonly ILogger<AzureKeyVaultProvider> _logger;

    /// <summary>Cria o provider sobre um <see cref="SecretClient" /> (injetado via DI).</summary>
    public AzureKeyVaultProvider(SecretClient client, ILogger<AzureKeyVaultProvider> logger) {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    ///     Cria um provider autônomo a partir das opções — para a fonte de <c>IConfiguration</c>, que roda
    ///     antes do contêiner de DI existir.
    /// </summary>
    public static AzureKeyVaultProvider Create(AzureKeyVaultOptions options) =>
        new(AzureKeyVaultClientFactory.Build(options), NullLogger<AzureKeyVaultProvider>.Instance);

    /// <inheritdoc />
    public async Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default) =>
        (await GetSecretWithMetadataAsync(name, cancellationToken))?.Value;

    /// <inheritdoc />
    public async Task<SecretValue?> GetSecretWithMetadataAsync(string name,
        CancellationToken cancellationToken = default) {
        try {
            var response = await _client.GetSecretAsync(name, cancellationToken: cancellationToken);
            var secret = response.Value;
            return new SecretValue(name, secret.Value, secret.Properties.Version, secret.Properties.UpdatedOn);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) {
            _logger.LogDebug("Segredo '{Secret}' não encontrado no Azure Key Vault.", name);
            return null;
        }
    }
}
