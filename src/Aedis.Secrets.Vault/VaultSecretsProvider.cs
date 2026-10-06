using System.Net;
using Aedis.Secrets.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VaultSharp;
using VaultSharp.Core;

namespace Aedis.Secrets.Vault;

/// <summary>
///     Provider de segredos sobre o HashiCorp Vault (KV v2). Lê o segredo no path informado e devolve o
///     campo <see cref="VaultOptions.ValueKey" />, com metadados (versão e <c>CreatedTime</c> como rotação).
///     Segredo inexistente (HTTP 404) devolve <c>null</c>; falhas transitórias do Vault sobem para o
///     chamador. Também implementa <see cref="ISecretsWriter" />: grava <c>{ ValueKey: valor }</c> como
///     nova versão do path e a remoção é o soft delete da versão corrente (recuperável via <c>undelete</c>).
///     Normalmente é envolvido pelo <c>CachingSecretsProvider</c> via DI.
/// </summary>
public sealed class VaultSecretsProvider : ISecretsProvider, ISecretsWriter
{
    private readonly IVaultClient _client;
    private readonly ILogger<VaultSecretsProvider> _logger;
    private readonly string _mountPoint;
    private readonly string _valueKey;

    /// <summary>Cria o provider sobre um <see cref="IVaultClient" /> (injetado via DI) e as opções.</summary>
    public VaultSecretsProvider(IVaultClient client, IOptions<VaultOptions> options,
        ILogger<VaultSecretsProvider> logger) {
        _client = client;
        _mountPoint = options.Value.MountPoint;
        _valueKey = options.Value.ValueKey;
        _logger = logger;
    }

    /// <summary>
    ///     Cria um provider autônomo a partir das opções — para a fonte de <c>IConfiguration</c>, que roda
    ///     antes do contêiner de DI existir.
    /// </summary>
    public static VaultSecretsProvider Create(VaultOptions options) =>
        new(VaultClientFactory.Build(options), Options.Create(options), NullLogger<VaultSecretsProvider>.Instance);

    /// <inheritdoc />
    public async Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default) =>
        (await GetSecretWithMetadataAsync(name, cancellationToken))?.Value;

    /// <inheritdoc />
    public async Task<SecretValue?> GetSecretWithMetadataAsync(string name,
        CancellationToken cancellationToken = default) {
        try {
            var secret = await _client.V1.Secrets.KeyValue.V2.ReadSecretAsync(name, mountPoint: _mountPoint);
            if (!secret.Data.Data.TryGetValue(_valueKey, out var raw) || raw is null)
                return null;

            var metadata = secret.Data.Metadata;
            var rotatedAt = DateTimeOffset.TryParse(metadata?.CreatedTime, out var parsed) ? parsed : (DateTimeOffset?)null;
            return new SecretValue(name, raw.ToString()!, metadata?.Version.ToString(), rotatedAt);
        }
        catch (VaultApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound) {
            _logger.LogDebug("Segredo '{Secret}' não encontrado no Vault.", name);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(string name, string value, CancellationToken cancellationToken = default) {
        var data = new Dictionary<string, object> { [_valueKey] = value };
        await _client.V1.Secrets.KeyValue.V2.WriteSecretAsync(name, data, mountPoint: _mountPoint);
        _logger.LogDebug("Segredo '{Secret}' gravado no Vault.", name);
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default) {
        try {
            await _client.V1.Secrets.KeyValue.V2.DeleteSecretAsync(name, mountPoint: _mountPoint);
            _logger.LogDebug("Versão corrente do segredo '{Secret}' removida no Vault.", name);
        }
        catch (VaultApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound) {
            _logger.LogDebug("Segredo '{Secret}' já não existia no Vault.", name);
        }
    }
}
