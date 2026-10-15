using System.Security.Cryptography;
using Aedis.Signing.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aedis.Signing;

/// <summary>
///     Chave ECDSA P-256 em processo — <strong>dev e testes</strong>. Vem de <c>Signing:InProcess:PrivateKeyPem</c>
///     ou é gerada na subida (<c>AllowEphemeralKey</c>). É provider de chave e de assinatura ao mesmo tempo;
///     a chave privada fica neste objeto e nunca é logada.
/// </summary>
public sealed class InProcessSigningKeyProvider : ISigningKeyProvider, ISignatureProvider, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<InProcessSigningKeyProvider> _logger;
    private readonly SigningOptions _options;
    private readonly SigningKeyState _state;
    private ECDsa? _key;

    /// <summary>Cria o provider a partir das opções (PEM ou efêmera).</summary>
    public InProcessSigningKeyProvider(SigningOptions options, SigningKeyState state, ILogger<InProcessSigningKeyProvider> logger) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Para testes: chave já pronta, sem passar pela configuração.</summary>
    public InProcessSigningKeyProvider(ECDsa key, string? keyId, SigningKeyState state, ILogger<InProcessSigningKeyProvider> logger)
        : this(new SigningOptions { InProcess = { KeyId = keyId, AllowEphemeralKey = true } }, state, logger) {
        _key = key ?? throw new ArgumentNullException(nameof(key));
    }

    /// <inheritdoc />
    public string Algorithm => SigningOptions.Algorithm;

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

            _key ??= LoadOrCreate();
            var parameters = _key.ExportParameters(false);
            if (parameters.Curve.Oid?.Value != ECCurve.NamedCurves.nistP256.Oid.Value && parameters.Curve.Oid?.FriendlyName != "nistP256")
                throw new SigningKeyInvalidStateException("in-process", "A chave de assinatura em processo precisa ser ECDSA P-256.");

            var jwkWithoutKid = JsonWebKeyDocument.FromEcPublicKey(parameters, "pending");
            var kid = _options.InProcess.KeyId ?? $"dev-{jwkWithoutKid.ComputeThumbprint()}";
            var handle = new SigningKeyHandle(kid, null, SigningOptions.Algorithm, parameters, jwkWithoutKid with { Kid = kid });
            _state.SetReady(handle);
            return handle;
        }
        finally {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> SignDigestAsync(ReadOnlyMemory<byte> digest, CancellationToken cancellationToken = default) {
        await GetKeyAsync(cancellationToken);
        var key = _key ?? throw new InvalidOperationException("Chave de assinatura não inicializada; chame EnsureKeyAsync primeiro.");
        return key.SignHash(digest.Span, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <inheritdoc />
    public void Dispose() {
        _key?.Dispose();
        _gate.Dispose();
    }

    private ECDsa LoadOrCreate() {
        var pem = _options.InProcess.PrivateKeyPem;
        if (!string.IsNullOrWhiteSpace(pem)) {
            var key = ECDsa.Create();
            try {
                key.ImportFromPem(pem);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException) {
                key.Dispose();
                throw new SigningKeyInvalidStateException("in-process", "Signing:InProcess:PrivateKeyPem não é um PEM válido de chave privada EC.", ex);
            }

            return key;
        }

        if (!_options.InProcess.AllowEphemeralKey)
            throw new SigningKeyNotFoundException("in-process", "Signing:InProcess:PrivateKeyPem está vazio e AllowEphemeralKey é false.");

        _logger.LogWarning("Assinando com chave EFÊMERA em processo: as assinaturas não serão verificáveis após o reinício. Nunca use fora de dev/testes.");
        return ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }
}
