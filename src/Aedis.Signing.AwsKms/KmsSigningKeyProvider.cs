using System.Security.Cryptography;
using Aedis.Exceptions;
using Aedis.Signing.Abstractions;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Microsoft.Extensions.Logging;

namespace Aedis.Signing.AwsKms;

/// <summary>
///     Resolve a chave de assinatura no AWS KMS, na ordem: (a) <c>KeyId</c> explícito; (b) o alias configurado,
///     se já existe; (c) com <c>AutoCreateKey</c>, cria a chave (<c>ECC_NIST_P256</c>/<c>SIGN_VERIFY</c>) e o
///     alias. A corrida entre réplicas no <c>CreateAlias</c> é resolvida a favor de quem chegou primeiro; a
///     chave órfã é agendada para exclusão. Também sonda a chave para o health check.
/// </summary>
public sealed class KmsSigningKeyProvider(
    IAmazonKeyManagementService kms,
    SigningOptions options,
    AwsKmsSigningOptions kmsOptions,
    SigningKeyState state,
    ILogger<KmsSigningKeyProvider> logger)
    : ISigningKeyProvider, ISigningKeyProbe
{
    /// <summary>Algoritmo de assinatura exigido da chave.</summary>
    public const string SigningAlgorithmName = "ECDSA_SHA_256";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IAmazonKeyManagementService _kms = kms ?? throw new ArgumentNullException(nameof(kms));
    private readonly AwsKmsSigningOptions _kmsOptions = kmsOptions ?? throw new ArgumentNullException(nameof(kmsOptions));
    private readonly ILogger<KmsSigningKeyProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SigningOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly SigningKeyState _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <inheritdoc />
    public bool TryGetKey(out SigningKeyHandle? handle) => _state.TryGet(out handle);

    /// <inheritdoc />
    public Task<SigningKeyHandle> GetKeyAsync(CancellationToken cancellationToken = default) =>
        _state.TryGet(out var handle) && handle is not null ? Task.FromResult(handle) : _state.WaitAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<SigningKeyHandle> EnsureKeyAsync(CancellationToken cancellationToken = default) {
        if (_state.TryGet(out var existing) && existing is not null) return existing;

        await _gate.WaitAsync(cancellationToken);
        try {
            if (_state.TryGet(out existing) && existing is not null) return existing;

            var metadata = await ResolveMetadataAsync(cancellationToken);
            Validate(metadata);

            var handle = await BuildHandleAsync(metadata, cancellationToken);
            _state.SetReady(handle);
            _logger.LogDebug("Chave de assinatura pronta: {KeyId} ({KeySpec}, alias {Alias}).",
                handle.KeyId, metadata.KeySpec, _kmsOptions.KeyAlias ?? "-");
            return handle;
        }
        finally {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SigningKeyProbeResult> ProbeAsync(SigningKeyHandle handle, CancellationToken cancellationToken = default) {
        var response = await AwsKmsExceptionMapper.WrapAsync(handle.KeyId,
            () => _kms.DescribeKeyAsync(new DescribeKeyRequest { KeyId = handle.ProviderReference }, cancellationToken), cancellationToken);
        var keyState = response.KeyMetadata.KeyState;
        return new SigningKeyProbeResult(keyState == KeyState.Enabled, keyState?.Value ?? "desconhecido");
    }

    private async Task<KeyMetadata> ResolveMetadataAsync(CancellationToken ct) {
        if (!string.IsNullOrWhiteSpace(_kmsOptions.KeyId))
            return await DescribeAsync(_kmsOptions.KeyId, ct);

        var alias = _kmsOptions.KeyAlias ?? throw new SigningException("Signing:AwsKms:KeyId ou Signing:AwsKms:KeyAlias precisa estar configurado.");

        try {
            return await DescribeAsync(alias, ct);
        }
        catch (SigningKeyNotFoundException) when (_kmsOptions.AutoCreateKey) {
            _logger.LogDebug("Alias {Alias} da chave de assinatura não existe; criando uma chave ECC_NIST_P256.", alias);
        }
        catch (SigningKeyNotFoundException ex) {
            throw new SigningKeyNotFoundException(alias,
                $"O alias KMS '{alias}' não existe. Provisione a chave com esse alias ou ligue Signing:AwsKms:AutoCreateKey.", ex);
        }

        return await CreateKeyAndAliasAsync(alias, ct);
    }

    private async Task<KeyMetadata> DescribeAsync(string keyId, CancellationToken ct) {
        var response = await AwsKmsExceptionMapper.WrapAsync(keyId,
            () => _kms.DescribeKeyAsync(new DescribeKeyRequest { KeyId = keyId }, ct), ct);
        return response.KeyMetadata;
    }

    private async Task<KeyMetadata> CreateKeyAndAliasAsync(string alias, CancellationToken ct) {
        var created = await AwsKmsExceptionMapper.WrapAsync(alias, () => _kms.CreateKeyAsync(BuildCreateKeyRequest(), ct), ct);
        var newKey = created.KeyMetadata;

        try {
            await AwsKmsExceptionMapper.WrapAsync(alias,
                () => _kms.CreateAliasAsync(new CreateAliasRequest { AliasName = alias, TargetKeyId = newKey.KeyId }, ct), ct);
            _logger.LogDebug("Chave de assinatura {KeyId} criada e alias {Alias} atribuído.", newKey.KeyId, alias);
            return newKey;
        }
        catch (AlreadyExistsException) {
            _logger.LogWarning("O alias {Alias} foi criado concorrentemente por outra réplica; usando a chave existente e agendando a exclusão de {OrphanKeyId}.",
                alias, newKey.KeyId);
            var winner = await DescribeWithRetryAsync(alias, ct);
            await ScheduleOrphanDeletionAsync(newKey.KeyId, ct);
            return winner;
        }
    }

    private CreateKeyRequest BuildCreateKeyRequest() {
        var app = _options.ApplicationName ?? "aedis";
        var tags = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["application"] = app,
            ["managed-by"] = "aedis",
            ["purpose"] = "signing"
        };
        foreach (var (k, v) in _kmsOptions.Tags) tags[k] = v;

        return new CreateKeyRequest {
            KeySpec = KeySpec.ECC_NIST_P256,
            KeyUsage = KeyUsageType.SIGN_VERIFY,
            Description = _kmsOptions.Description ?? $"Signing key ({app})",
            Tags = tags.Select(t => new Tag { TagKey = t.Key, TagValue = t.Value }).ToList()
        };
    }

    private async Task<KeyMetadata> DescribeWithRetryAsync(string alias, CancellationToken ct) {
        for (var attempt = 1;; attempt++)
            try {
                return await DescribeAsync(alias, ct);
            }
            catch (SigningKeyNotFoundException) when (attempt < 3) {
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            }
    }

    private async Task ScheduleOrphanDeletionAsync(string keyId, CancellationToken ct) {
        try {
            await _kms.ScheduleKeyDeletionAsync(new ScheduleKeyDeletionRequest {
                KeyId = keyId, PendingWindowInDays = _kmsOptions.OrphanKeyDeletionDays
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogWarning(ex, "Não foi possível agendar a exclusão da chave órfã {KeyId}; remova-a manualmente.", keyId);
        }
    }

    private static void Validate(KeyMetadata metadata) {
        var id = metadata.KeyId;

        if (metadata.KeyState == KeyState.Creating || metadata.KeyState == KeyState.Updating)
            throw new ServiceTemporarilyUnavailableException(AwsKmsExceptionMapper.ServiceName,
                $"A chave KMS '{id}' ainda está {metadata.KeyState}; nova tentativa.");

        if (metadata.KeyState != KeyState.Enabled)
            throw new SigningKeyInvalidStateException(id, $"A chave KMS '{id}' está {metadata.KeyState}; precisa estar Enabled.");

        if (metadata.KeySpec != KeySpec.ECC_NIST_P256)
            throw new SigningKeyInvalidStateException(id, $"A chave KMS '{id}' tem spec {metadata.KeySpec}; ECC_NIST_P256 é obrigatório.");

        if (metadata.KeyUsage != KeyUsageType.SIGN_VERIFY)
            throw new SigningKeyInvalidStateException(id, $"A chave KMS '{id}' tem uso {metadata.KeyUsage}; SIGN_VERIFY é obrigatório.");

        if (metadata.SigningAlgorithms is { Count: > 0 } algorithms && !algorithms.Contains(SigningAlgorithmName))
            throw new SigningKeyInvalidStateException(id, $"A chave KMS '{id}' não suporta {SigningAlgorithmName}.");
    }

    private async Task<SigningKeyHandle> BuildHandleAsync(KeyMetadata metadata, CancellationToken ct) {
        var response = await AwsKmsExceptionMapper.WrapAsync(metadata.KeyId,
            () => _kms.GetPublicKeyAsync(new GetPublicKeyRequest { KeyId = metadata.Arn ?? metadata.KeyId }, ct), ct);

        using var ecdsa = ECDsa.Create();
        try {
            ecdsa.ImportSubjectPublicKeyInfo(response.PublicKey.ToArray(), out _);
        }
        catch (CryptographicException ex) {
            throw new SigningKeyInvalidStateException(metadata.KeyId, $"A chave pública da chave KMS '{metadata.KeyId}' não é um SubjectPublicKeyInfo válido.", ex);
        }

        var parameters = ecdsa.ExportParameters(false);
        var jwk = JsonWebKeyDocument.FromEcPublicKey(parameters, metadata.KeyId);
        return new SigningKeyHandle(metadata.KeyId, metadata.Arn, SigningOptions.Algorithm, parameters, jwk);
    }
}
