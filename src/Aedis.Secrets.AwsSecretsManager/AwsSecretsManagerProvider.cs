using Aedis.Secrets.Abstractions;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aedis.Secrets.AwsSecretsManager;

/// <summary>
///     Provider de segredos sobre o AWS Secrets Manager. Lê <c>SecretString</c> (ou <c>SecretBinary</c> em
///     base64) e expõe metadados (<c>VersionId</c>, data da versão como rotação). Segredo inexistente
///     (<see cref="ResourceNotFoundException" />) devolve <c>null</c>; falhas transitórias da AWS sobem para
///     o chamador. Também implementa <see cref="ISecretsWriter" />: cria o segredo (com a chave KMS das
///     opções) ou grava uma versão nova quando ele já existe, e remove respeitando a janela de recuperação.
///     Normalmente é envolvido pelo <c>CachingSecretsProvider</c> via DI.
/// </summary>
public sealed class AwsSecretsManagerProvider : ISecretsProvider, ISecretsWriter
{
    private readonly IAmazonSecretsManager _client;
    private readonly ILogger<AwsSecretsManagerProvider> _logger;
    private readonly AwsSecretsManagerOptions _options;

    /// <summary>Cria o provider sobre um cliente do Secrets Manager e as opções (injetados via DI).</summary>
    public AwsSecretsManagerProvider(IAmazonSecretsManager client, IOptions<AwsSecretsManagerOptions> options,
        ILogger<AwsSecretsManagerProvider> logger) {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    ///     Cria um provider autônomo a partir das opções — para a fonte de <c>IConfiguration</c>, que roda
    ///     antes do contêiner de DI existir.
    /// </summary>
    public static AwsSecretsManagerProvider Create(AwsSecretsManagerOptions options) =>
        new(AwsSecretsManagerClientFactory.Build(options), Options.Create(options),
            NullLogger<AwsSecretsManagerProvider>.Instance);

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

    /// <inheritdoc />
    public async Task SetSecretAsync(string name, string value, CancellationToken cancellationToken = default) {
        try {
            await _client.CreateSecretAsync(new CreateSecretRequest {
                Name = name,
                SecretString = value,
                KmsKeyId = string.IsNullOrWhiteSpace(_options.KmsKeyId) ? null : _options.KmsKeyId
            }, cancellationToken);
            _logger.LogDebug("Segredo '{Secret}' criado no AWS Secrets Manager.", name);
        }
        catch (ResourceExistsException) {
            await _client.PutSecretValueAsync(new PutSecretValueRequest { SecretId = name, SecretString = value },
                cancellationToken);
            _logger.LogDebug("Nova versão do segredo '{Secret}' gravada no AWS Secrets Manager.", name);
        }
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default) {
        try {
            await _client.DeleteSecretAsync(new DeleteSecretRequest {
                SecretId = name,
                RecoveryWindowInDays = _options.DeletionRecoveryWindowDays
            }, cancellationToken);
            _logger.LogDebug("Segredo '{Secret}' agendado para remoção no AWS Secrets Manager.", name);
        }
        catch (ResourceNotFoundException) {
            _logger.LogDebug("Segredo '{Secret}' já não existia no AWS Secrets Manager.", name);
        }
    }

    private static string? ReadBinary(GetSecretValueResponse response) =>
        response.SecretBinary is { } binary ? Convert.ToBase64String(binary.ToArray()) : null;
}
