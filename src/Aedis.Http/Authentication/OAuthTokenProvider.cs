using System.Text.Json.Serialization;
using Aedis.Http.Abstractions;
using Aedis.Http.Abstractions.Authentication;
using Microsoft.Extensions.Logging;

namespace Aedis.Http.Authentication;

/// <summary>
///     Template agnóstico de provedor de token OAuth2 <c>client_credentials</c> com renovação proativa.
///     Quando há um token válido, ele é servido imediatamente; ao entrar na janela de renovação
///     (<c>RefreshAt</c>, antes da expiração real), dispara-se uma renovação em <strong>segundo plano</strong>
///     enquanto o token atual segue sendo servido — nenhuma requisição espera por um token. A geração é
///     coordenada para evitar race condition: single-flight em processo (uma busca por instância) e lock de
///     geração via <see cref="ITokenStore.TryAcquireRefreshLockAsync" /> (uma busca por toda a frota, quando o
///     store é distribuído). Em cache frio, a busca é síncrona; sob falha, o erro propaga (fail-fast).
/// </summary>
public sealed class OAuthTokenProvider : ITokenProvider
{
    private readonly IAedisHttpClient _httpClient;
    private readonly ITokenStore _tokenStore;
    private readonly OAuthTokenOptions _options;
    private readonly IAuthenticationStrategy _strategy;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OAuthTokenProvider>? _logger;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    /// <summary>
    ///     Cria o provedor a partir da fábrica de clientes, do store e das opções. O cliente de obtenção de
    ///     token é criado uma vez (reuso eficiente) sobre o transporte das opções.
    /// </summary>
    public OAuthTokenProvider(
        IAedisHttpClientFactory httpClientFactory,
        ITokenStore tokenStore,
        OAuthTokenOptions options,
        TimeProvider? timeProvider = null,
        ILogger<OAuthTokenProvider>? logger = null) {
        if (string.IsNullOrWhiteSpace(options.TokenEndpoint))
            throw new ArgumentException("OAuthTokenOptions.TokenEndpoint é obrigatório.", nameof(options));

        _strategy = options.Strategy ?? throw new ArgumentException("OAuthTokenOptions.Strategy é obrigatório.", nameof(options));
        _httpClient = httpClientFactory.Create(options.Transport);
        _tokenStore = tokenStore;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default) {
        var now = _timeProvider.GetUtcNow();
        var cached = await _tokenStore.GetAsync(_options.CacheKey, cancellationToken);

        if (cached is not null && !cached.IsExpired(now)) {
            if (cached.IsDueForRefresh(now))
                TriggerBackgroundRefresh();

            return cached.AccessToken;
        }

        return await AcquireTokenAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        _tokenStore.RemoveAsync(_options.CacheKey, cancellationToken);

    /// <summary>
    ///     Renova o token sob o lock de geração: se outra chamada/instância já o detém, não faz nada (o token
    ///     atual continua válido e sendo servido). Usada pela renovação proativa em segundo plano.
    /// </summary>
    internal async Task RefreshAsync(CancellationToken cancellationToken = default) {
        await using var lease = await _tokenStore.TryAcquireRefreshLockAsync(_options.CacheKey, _options.FetchLockDuration, cancellationToken);
        if (lease is null)
            return;

        var cached = await _tokenStore.GetAsync(_options.CacheKey, cancellationToken);
        if (cached is not null && !cached.IsDueForRefresh(_timeProvider.GetUtcNow()))
            return;

        await FetchAndStoreAsync(cancellationToken);
    }

    private async Task<string> AcquireTokenAsync(CancellationToken cancellationToken) {
        await _fetchGate.WaitAsync(cancellationToken);
        try {
            var cached = await _tokenStore.GetAsync(_options.CacheKey, cancellationToken);
            if (cached is not null && !cached.IsExpired(_timeProvider.GetUtcNow()))
                return cached.AccessToken;

            await using var lease = await _tokenStore.TryAcquireRefreshLockAsync(_options.CacheKey, _options.FetchLockDuration, cancellationToken);
            if (lease is null) {
                var awaited = await AwaitTokenFromOtherInstanceAsync(cancellationToken);
                if (awaited is not null)
                    return awaited;
            }

            return await FetchAndStoreAsync(cancellationToken);
        }
        finally {
            _fetchGate.Release();
        }
    }

    private void TriggerBackgroundRefresh() =>
        _ = Task.Run(async () => {
            try {
                await RefreshAsync(CancellationToken.None);
            }
            catch (Exception exception) {
                _logger?.LogWarning(exception, "Falha ao renovar o token em segundo plano; será renovado sob demanda.");
            }
        });

    private async Task<string?> AwaitTokenFromOtherInstanceAsync(CancellationToken cancellationToken) {
        for (var attempt = 0; attempt < 20; attempt++) {
            await Task.Delay(50, cancellationToken);
            var cached = await _tokenStore.GetAsync(_options.CacheKey, cancellationToken);
            if (cached is not null && !cached.IsExpired(_timeProvider.GetUtcNow()))
                return cached.AccessToken;
        }

        return null;
    }

    private async Task<string> FetchAndStoreAsync(CancellationToken cancellationToken) {
        var (accessToken, expiresInSeconds) = await FetchTokenAsync(cancellationToken);
        var token = StampToken(accessToken, expiresInSeconds);
        await _tokenStore.SetAsync(_options.CacheKey, token, cancellationToken);
        return token.AccessToken;
    }

    private async Task<(string AccessToken, int ExpiresInSeconds)> FetchTokenAsync(CancellationToken cancellationToken) {
        var request = _strategy.BuildTokenRequest(_options.TokenEndpoint);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha ao obter token ({response.StatusCode}): {response.ReadAsString()}");

        var payload = response.ReadFromJson<TokenResponse>()
                      ?? throw new InvalidOperationException("Resposta do endpoint de token vazia ou inválida.");

        if (string.IsNullOrEmpty(payload.AccessToken))
            throw new InvalidOperationException("Resposta do endpoint de token sem access_token.");

        return (payload.AccessToken, payload.ExpiresIn);
    }

    /// <summary>
    ///     Carimba o token com seus instantes de expiração e renovação: vida = <c>max(60s, expires_in)</c>;
    ///     a renovação proativa ocorre a vida menos a margem de segurança, limitada entre o piso e o teto
    ///     configurados e nunca após a expiração.
    /// </summary>
    internal CachedToken StampToken(string accessToken, int expiresInSeconds) {
        var now = _timeProvider.GetUtcNow();
        var lifetime = TimeSpan.FromSeconds(Math.Max(60, expiresInSeconds));

        var untilRefresh = lifetime - _options.ExpirationSkew;
        if (untilRefresh < _options.MinimumLifetime)
            untilRefresh = _options.MinimumLifetime;
        if (untilRefresh > _options.MaximumLifetime)
            untilRefresh = _options.MaximumLifetime;
        if (untilRefresh > lifetime)
            untilRefresh = lifetime;

        return new CachedToken(accessToken, now + lifetime, now + untilRefresh);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
