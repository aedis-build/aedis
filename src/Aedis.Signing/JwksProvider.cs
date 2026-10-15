using Aedis.Signing.Abstractions;

namespace Aedis.Signing;

/// <summary>JWKS montado a partir da chave resolvida no bootstrap. Nunca consulta o provider por requisição.</summary>
public sealed class JwksProvider(ISigningKeyProvider keyProvider) : IJwksProvider
{
    private readonly ISigningKeyProvider _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));

    /// <inheritdoc />
    public bool TryGet(out JsonWebKeySetDocument? document) {
        if (_keyProvider.TryGetKey(out var handle) && handle is not null) {
            document = Build(handle);
            return true;
        }

        document = null;
        return false;
    }

    /// <inheritdoc />
    public async Task<JsonWebKeySetDocument> GetAsync(CancellationToken cancellationToken = default) =>
        Build(await _keyProvider.GetKeyAsync(cancellationToken));

    private static JsonWebKeySetDocument Build(SigningKeyHandle handle) => new() { Keys = [handle.Jwk] };
}
