using System.Security.Cryptography;
using Aedis.Signing.Abstractions;
using NSign;
using NSign.Signatures;

namespace Aedis.Signing;

/// <summary>
///     Adapta a costura de fornecedor (<see cref="ISignatureProvider" />) ao <see cref="ISigner" /> da NSign:
///     faz o SHA-256 da base de assinatura aqui e entrega só o digest ao provider, que devolve <c>r || s</c>.
///     Os parâmetros <c>keyid</c>/<c>alg</c> vêm da chave resolvida.
/// </summary>
internal sealed class SignatureProviderSigner(ISigningKeyProvider keyProvider, ISignatureProvider signatureProvider) : ISigner
{
    private readonly ISigningKeyProvider _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
    private readonly ISignatureProvider _signatureProvider = signatureProvider ?? throw new ArgumentNullException(nameof(signatureProvider));

    public void UpdateSignatureParams(SignatureParamsComponent signatureParams) {
        ArgumentNullException.ThrowIfNull(signatureParams);
        if (!_keyProvider.TryGetKey(out var handle) || handle is null)
            throw new InvalidOperationException("Chave de assinatura não resolvida; aguarde o bootstrap antes de assinar.");

        signatureParams.WithKeyId(handle.KeyId);
        signatureParams.Algorithm = handle.Algorithm;
    }

    public async Task<ReadOnlyMemory<byte>> SignAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken) {
        await _keyProvider.GetKeyAsync(cancellationToken);
        var digest = SHA256.HashData(input.Span);
        return await _signatureProvider.SignDigestAsync(digest, cancellationToken);
    }
}
