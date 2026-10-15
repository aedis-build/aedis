using System.Security.Cryptography;
using Aedis.Signing.Abstractions;

namespace Aedis.Signing.Verification;

/// <summary>JWKS fixo (testes, ou receptor que recebeu o documento fora de banda).</summary>
public sealed class StaticJwksKeyResolver(JsonWebKeySetDocument document) : ISigningPublicKeyResolver
{
    private readonly JsonWebKeySetDocument _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <inheritdoc />
    public Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_document.FindByKid(keyId)?.ToECDsa());
}

/// <summary>
///     JWKS remoto (a URL publicada pelo emissor) com cache em memória. Um <c>kid</c> desconhecido força um
///     refetch único — é o caminho normal de rotação de chave.
/// </summary>
public sealed class RemoteJwksKeyResolver(HttpClient httpClient, Uri jwksUri, TimeSpan? cacheTtl = null, TimeProvider? timeProvider = null)
    : ISigningPublicKeyResolver
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly Uri _jwksUri = jwksUri ?? throw new ArgumentNullException(nameof(jwksUri));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _ttl = cacheTtl ?? TimeSpan.FromHours(1);
    private JsonWebKeySetDocument? _cached;
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public async Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken = default) {
        var document = await GetDocumentAsync(false, cancellationToken);
        var jwk = document.FindByKid(keyId);

        if (jwk is null) {
            document = await GetDocumentAsync(true, cancellationToken);
            jwk = document.FindByKid(keyId);
        }

        return jwk?.ToECDsa();
    }

    private async Task<JsonWebKeySetDocument> GetDocumentAsync(bool forceRefresh, CancellationToken cancellationToken) {
        var now = _timeProvider.GetUtcNow();
        if (!forceRefresh && _cached is not null && now - _fetchedAt < _ttl) return _cached;

        await _gate.WaitAsync(cancellationToken);
        try {
            now = _timeProvider.GetUtcNow();
            if (!forceRefresh && _cached is not null && now - _fetchedAt < _ttl) return _cached;

            using var response = await _httpClient.GetAsync(_jwksUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            _cached = JsonWebKeySetDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            _fetchedAt = now;
            return _cached;
        }
        finally {
            _gate.Release();
        }
    }
}
