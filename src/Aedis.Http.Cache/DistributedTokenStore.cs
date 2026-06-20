using Aedis.Cache.Abstractions;
using Aedis.Http.Abstractions.Authentication;

namespace Aedis.Http.Cache;

/// <summary>
///     Armazenamento de token sobre o <see cref="ICache" /> do Aedis (ex.: Redis), permitindo compartilhar o
///     token entre todas as instâncias do serviço — uma busca de token vale para a frota inteira, em vez de
///     uma por processo. A expiração é o TTL da entrada no cache. Registre via
///     <c>AddAedisDistributedTokenStore</c> para substituir o store em memória default.
/// </summary>
public sealed class DistributedTokenStore : ITokenStore
{
    private readonly ICache _cache;

    /// <summary>Cria o store sobre o <see cref="ICache" /> registrado pela aplicação.</summary>
    public DistributedTokenStore(ICache cache) => _cache = cache;

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        _cache.GetStringAsync(key, cancellationToken);

    /// <inheritdoc />
    public Task SetAsync(string key, string token, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        _cache.SetStringAsync(key, token, ttl, cancellationToken);

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        await _cache.RemoveAsync(key, cancellationToken);
}
