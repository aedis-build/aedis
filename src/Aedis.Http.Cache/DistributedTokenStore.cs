using System.Text.Json;
using System.Text.Json.Serialization;
using Aedis.Cache.Abstractions;
using Aedis.Http.Abstractions.Authentication;

namespace Aedis.Http.Cache;

/// <summary>
///     Armazenamento de token sobre o <see cref="ICache" /> do Aedis (ex.: Redis), compartilhando o token e o
///     lock de geração entre todas as instâncias do serviço — uma busca de token vale para a frota inteira. O
///     token é serializado como um pequeno envelope JSON (com os instantes de expiração/renovação) e a entrada
///     vive até a expiração real. O lock de geração mapeia para o lock distribuído do cache
///     (<see cref="ICache.IsLeaderAsync" />), garantindo uma única geração concorrente. Registre via
///     <c>AddAedisDistributedTokenStore</c> para substituir o store em memória default.
/// </summary>
public sealed class DistributedTokenStore : ITokenStore
{
    private const string RefreshLockSuffix = ":refresh-lock";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ICache _cache;
    private readonly TimeProvider _timeProvider;

    /// <summary>Cria o store sobre o <see cref="ICache" /> registrado, usando o <see cref="TimeProvider" /> informado (ou o do sistema).</summary>
    public DistributedTokenStore(ICache cache, TimeProvider? timeProvider = null) {
        _cache = cache;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<CachedToken?> GetAsync(string key, CancellationToken cancellationToken = default) {
        var json = await _cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrEmpty(json))
            return null;

        var envelope = JsonSerializer.Deserialize<Envelope>(json, Json);
        if (envelope is null)
            return null;

        var token = new CachedToken(
            envelope.AccessToken,
            DateTimeOffset.FromUnixTimeMilliseconds(envelope.ExpiresAt),
            DateTimeOffset.FromUnixTimeMilliseconds(envelope.RefreshAt));

        return token.IsExpired(_timeProvider.GetUtcNow()) ? null : token;
    }

    /// <inheritdoc />
    public Task SetAsync(string key, CachedToken token, CancellationToken cancellationToken = default) {
        var ttl = token.ExpiresAt - _timeProvider.GetUtcNow();
        if (ttl <= TimeSpan.Zero)
            return Task.CompletedTask;

        var envelope = new Envelope(token.AccessToken, token.ExpiresAt.ToUnixTimeMilliseconds(), token.RefreshAt.ToUnixTimeMilliseconds());
        return _cache.SetStringAsync(key, JsonSerializer.Serialize(envelope, Json), ttl, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        await _cache.RemoveAsync(key, cancellationToken);

    /// <inheritdoc />
    public Task<IAsyncDisposable?> TryAcquireRefreshLockAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        _cache.IsLeaderAsync($"{key}{RefreshLockSuffix}", ttl, cancellationToken);

    private sealed record Envelope(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_at")] long ExpiresAt,
        [property: JsonPropertyName("refresh_at")] long RefreshAt);
}
