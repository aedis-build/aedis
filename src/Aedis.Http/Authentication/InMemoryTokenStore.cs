using System.Collections.Concurrent;
using Aedis.Http.Abstractions.Authentication;

namespace Aedis.Http.Authentication;

/// <summary>
///     Armazenamento de token em memória (por processo) — o default quando não há cache distribuído. Cada
///     entrada guarda o token e seu instante de expiração; uma leitura após a expiração devolve <c>null</c>,
///     disparando a renovação. Thread-safe. Para compartilhar o token entre instâncias, registre um store
///     distribuído (ex.: <c>Aedis.Http.Cache</c> sobre o <c>ICache</c>).
/// </summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>Cria o store usando o <see cref="TimeProvider" /> informado (ou o do sistema), útil para testes.</summary>
    public InMemoryTokenStore(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) {
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > _timeProvider.GetUtcNow())
            return Task.FromResult<string?>(entry.Token);

        _entries.TryRemove(key, out _);
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, string token, TimeSpan ttl, CancellationToken cancellationToken = default) {
        _entries[key] = new Entry(token, _timeProvider.GetUtcNow() + ttl);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private readonly record struct Entry(string Token, DateTimeOffset ExpiresAt);
}
