namespace Aedis.Cache.Abstractions;

public interface ICache
{
    Task<IAsyncDisposable?> IsLeaderAsync(string key, TimeSpan expiration,
        CancellationToken cancellationToken = default);

    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    Task SetStringAsync(string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default);

    Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiration,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> RenewLockAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<IEnumerable<string>> GetKeysAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrementa atomicamente um contador. No primeiro incremento (retorno == 1), define o TTL da chave.
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);
}