using Aedis.Secrets.Abstractions;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aedis.Secrets.AwsSecretsManager;

/// <summary>
///     Provider de segredos sobre o AWS Secrets Manager. Lê <c>SecretString</c> (ou <c>SecretBinary</c> em
///     base64) e expõe metadados (<c>VersionId</c>, data da versão como rotação). Segredo inexistente
///     (<see cref="ResourceNotFoundException" />) devolve <c>null</c>; falhas transitórias da AWS sobem para
///     o chamador. Normalmente é envolvido pelo <c>CachingSecretsProvider</c> via DI.
/// </summary>
public sealed class AwsSecretsManagerProvider : ISecretsProvider
{
    private readonly IAmazonSecretsManager _client;
    private readonly ILogger<AwsSecretsManagerProvider> _logger;

    /// <summary>Cria o provider sobre um cliente do Secrets Manager (injetado via DI).</summary>
    public AwsSecretsManagerProvider(IAmazonSecretsManager client, ILogger<AwsSecretsManagerProvider> logger) {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    ///     Cria um provider autônomo a partir das opções — para a fonte de <c>IConfiguration</c>, que roda
    ///     antes do contêiner de DI existir.
    /// </summary>
    public static AwsSecretsManagerProvider Create(AwsSecretsManagerOptions options) =>
        new(AwsSecretsManagerClientFactory.Build(options), NullLogger<AwsSecretsManagerProvider>.Instance);

    /// <inheritdoc />
    public async Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default) =>
        (await GetSecretWithMetadataAsync(name, cancellationToken))?.Value;

    /// <inheritdoc />
    public async Task<SecretValue?> GetSecretWithMetadataAsync(string name,
        CancellationToken cancellationToken = default) {
        try {
            var response = await _client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = name },
                cancellationToken);

            var value = response.SecretString ?? ReadBinary(response);
            if (value is null)
                return null;

            var rotatedAt = response.CreatedDate is { } createdDate
                ? new DateTimeOffset(DateTime.SpecifyKind(createdDate, DateTimeKind.Utc))
                : (DateTimeOffset?)null;
            return new SecretValue(name, value, response.VersionId, rotatedAt);
        }
        catch (ResourceNotFoundException) {
            _logger.LogDebug("Segredo '{Secret}' não encontrado no AWS Secrets Manager.", name);
            return null;
        }
    }

    private static string? ReadBinary(GetSecretValueResponse response) =>
        response.SecretBinary is { } binary ? Convert.ToBase64String(binary.ToArray()) : null;
}
