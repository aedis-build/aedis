using System.Text.Json.Serialization;
using Aedis.Http.Abstractions;
using Aedis.Http.Abstractions.Authentication;

namespace Aedis.Http.Authentication;

/// <summary>
///     Template agnóstico de provedor de token OAuth2 <c>client_credentials</c>. Implementa o ciclo
///     get-or-refresh: lê do <see cref="ITokenStore" />; em falta, obtém um novo token usando a
///     <see cref="IAuthenticationStrategy" /> configurada e o cacheia com um TTL derivado do
///     <c>expires_in</c> (menos a margem de segurança, limitado entre piso e teto). A obtenção é protegida por
///     um portão de concorrência (single-flight): chamadas paralelas em cache frio resultam em uma única
///     busca, evitando o cache stampede. Funciona com qualquer <see cref="IAedisHttpClient" /> (nativo ou Flurl).
/// </summary>
public sealed class OAuthTokenProvider : ITokenProvider
{
    private readonly IAedisHttpClient _httpClient;
    private readonly ITokenStore _tokenStore;
    private readonly OAuthTokenOptions _options;
    private readonly IAuthenticationStrategy _strategy;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    /// <summary>
    ///     Cria o provedor a partir da fábrica de clientes, do store e das opções. O cliente de obtenção de
    ///     token é criado uma vez (reuso eficiente) sobre o transporte das opções.
    /// </summary>
    public OAuthTokenProvider(IAedisHttpClientFactory httpClientFactory, ITokenStore tokenStore, OAuthTokenOptions options) {
        if (string.IsNullOrWhiteSpace(options.TokenEndpoint))
            throw new ArgumentException("OAuthTokenOptions.TokenEndpoint é obrigatório.", nameof(options));

        _strategy = options.Strategy ?? throw new ArgumentException("OAuthTokenOptions.Strategy é obrigatório.", nameof(options));
        _httpClient = httpClientFactory.Create(options.Transport);
        _tokenStore = tokenStore;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default) {
        var cached = await _tokenStore.GetAsync(_options.CacheKey, cancellationToken);
        if (cached is not null)
            return cached;

        await _fetchGate.WaitAsync(cancellationToken);
        try {
            cached = await _tokenStore.GetAsync(_options.CacheKey, cancellationToken);
            if (cached is not null)
                return cached;

            var (token, ttl) = await FetchTokenAsync(cancellationToken);
            await _tokenStore.SetAsync(_options.CacheKey, token, ttl, cancellationToken);
            return token;
        }
        finally {
            _fetchGate.Release();
        }
    }

    /// <inheritdoc />
    public Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        _tokenStore.RemoveAsync(_options.CacheKey, cancellationToken);

    private async Task<(string Token, TimeSpan Ttl)> FetchTokenAsync(CancellationToken cancellationToken) {
        var request = _strategy.BuildTokenRequest(_options.TokenEndpoint);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha ao obter token ({response.StatusCode}): {response.ReadAsString()}");

        var payload = response.ReadFromJson<TokenResponse>()
                      ?? throw new InvalidOperationException("Resposta do endpoint de token vazia ou inválida.");

        if (string.IsNullOrEmpty(payload.AccessToken))
            throw new InvalidOperationException("Resposta do endpoint de token sem access_token.");

        return (payload.AccessToken, CalculateTtl(payload.ExpiresIn));
    }

    /// <summary>
    ///     Calcula o TTL de cache do token: parte de <c>max(60s, expires_in)</c>, subtrai a margem de
    ///     segurança e limita entre o piso e o teto configurados.
    /// </summary>
    internal TimeSpan CalculateTtl(int expiresInSeconds) {
        var lifetime = TimeSpan.FromSeconds(Math.Max(60, expiresInSeconds));
        var ttl = lifetime - _options.ExpirationSkew;

        if (ttl < _options.MinimumLifetime)
            return _options.MinimumLifetime;

        return ttl > _options.MaximumLifetime ? _options.MaximumLifetime : ttl;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
