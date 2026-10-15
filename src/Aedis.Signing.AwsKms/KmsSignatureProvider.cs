using Aedis.Signing.Abstractions;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;

namespace Aedis.Signing.AwsKms;

/// <summary>
///     <see cref="ISignatureProvider" /> sobre o AWS KMS: manda só o digest (<c>MessageType=DIGEST</c>,
///     <c>ECDSA_SHA_256</c>) e converte a assinatura DER do KMS para <c>r || s</c>. A chave privada nunca sai do HSM.
/// </summary>
public sealed class KmsSignatureProvider(IAmazonKeyManagementService kms, ISigningKeyProvider keyProvider) : ISignatureProvider
{
    private readonly ISigningKeyProvider _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
    private readonly IAmazonKeyManagementService _kms = kms ?? throw new ArgumentNullException(nameof(kms));

    /// <inheritdoc />
    public string Algorithm => SigningOptions.Algorithm;

    /// <inheritdoc />
    public async Task<byte[]> SignDigestAsync(ReadOnlyMemory<byte> digest, CancellationToken cancellationToken = default) {
        var handle = await _keyProvider.GetKeyAsync(cancellationToken);

        var response = await AwsKmsExceptionMapper.WrapAsync(handle.KeyId, () => _kms.SignAsync(new SignRequest {
            KeyId = handle.ProviderReference,
            Message = new MemoryStream(digest.ToArray()),
            MessageType = MessageType.DIGEST,
            SigningAlgorithm = SigningAlgorithmSpec.ECDSA_SHA_256
        }, cancellationToken), cancellationToken);

        return EcdsaSignatureEncoding.DerToIeeeP1363(response.Signature.ToArray());
    }
}
