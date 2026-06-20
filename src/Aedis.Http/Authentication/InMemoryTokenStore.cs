using System.Collections.Concurrent;
using Aedis.Http.Abstractions.Authentication;

namespace Aedis.Http.Authentication;

/// <summary>
///     Armazenamento de token em memória (por processo) — o default quando não há cache distribuído. Guarda
///     o <see cref="CachedToken" /> com seus instantes de expiração/renovação e oferece um lock de geração
///     por chave (semáforo em processo) para que apenas uma chamada busque/renove o token por vez. Para
///     compartilhar token e lock entre instâncias, registre um store distribuído (ex.: <c>Aedis.Http.Cache</c>).
/// </summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, CachedToken> _entries = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>Cria o store usando o <see cref="TimeProvider" /> informado (ou o do sistema), útil para testes.</summary>
    public InMemoryTokenStore(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<CachedToken?> GetAsync(string key, CancellationToken cancellationToken = default) {
        if (_entries.TryGetValue(key, out var token) && !token.IsExpired(_timeProvider.GetUtcNow()))
            return Task.FromResult<CachedToken?>(token);

        _entries.TryRemove(key, out _);
        return Task.FromResult<CachedToken?>(null);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, CachedToken token, CancellationToken cancellationToken = default) {
        _entries[key] = token;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IAsyncDisposable?> TryAcquireRefreshLockAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default) {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        var acquired = gate.Wait(0, cancellationToken);
        return Task.FromResult<IAsyncDisposable?>(acquired ? new Lease(gate) : null);
    }

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
