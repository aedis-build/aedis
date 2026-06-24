using System.Collections.Concurrent;
using Aedis.Secrets.Abstractions;

namespace Aedis.Secrets;

/// <summary>
///     Decorator agnóstico que cacheia em memória os segredos lidos de um <see cref="ISecretsProvider" />
///     interno, com TTL por segredo e <strong>single-flight</strong> por nome (sob concorrência, só uma
///     chamada vai ao cofre por segredo; as demais aguardam e reaproveitam). Apenas segredos encontrados são
///     cacheados — leituras de segredo inexistente (<c>null</c>) passam direto, sem negative cache, para que
///     um segredo recém-criado seja visto na próxima leitura. Use <see cref="Invalidate" /> para forçar
///     releitura após uma rotação.
/// </summary>
public sealed class CachingSecretsProvider : ISecretsProvider
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ISecretsProvider _inner;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly TimeSpan _ttl;

    /// <summary>Cria o decorator sobre o provider <paramref name="inner" />, com janela de cache <paramref name="ttl" />.</summary>
    public CachingSecretsProvider(ISecretsProvider inner, TimeSpan ttl) {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ttl = ttl;
    }

    /// <inheritdoc />
    public async Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default) =>
        (await GetSecretWithMetadataAsync(name, cancellationToken))?.Value;

    /// <inheritdoc />
    public async Task<SecretValue?> GetSecretWithMetadataAsync(string name,
        CancellationToken cancellationToken = default) {
        if (TryGetFresh(name, out var cached))
            return cached;

        var gate = _locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try {
            if (TryGetFresh(name, out cached))
                return cached;

            var value = await _inner.GetSecretWithMetadataAsync(name, cancellationToken);
            if (value is not null)
                _cache[name] = new CacheEntry(value, DateTimeOffset.UtcNow + _ttl);
            return value;
        }
        finally {
            gate.Release();
        }
    }

    /// <summary>Remove o segredo <paramref name="name" /> do cache, forçando releitura do cofre na próxima leitura.</summary>
    public void Invalidate(string name) => _cache.TryRemove(name, out _);

    /// <summary>Esvazia todo o cache de segredos.</summary>
    public void Clear() => _cache.Clear();

    private bool TryGetFresh(string name, out SecretValue? value) {
        if (_cache.TryGetValue(name, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow) {
            value = entry.Value;
            return true;
        }

        value = null;
        return false;
    }

    private readonly record struct CacheEntry(SecretValue Value, DateTimeOffset ExpiresAt);
}
