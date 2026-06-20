namespace Aedis.Http.Abstractions.Authentication;

/// <summary>
///     Configura um provedor de token OAuth2 <c>client_credentials</c>: endpoint, estratégia de credencial,
///     perfil de transporte (incl. mTLS), chave de cache e a política de renovação. O token é renovado de
///     forma <strong>proativa</strong>: a partir de <see cref="ExpirationSkew" /> antes da expiração real
///     (derivada do <c>expires_in</c>), uma renovação ocorre em segundo plano enquanto o token atual,
///     ainda válido, continua sendo servido. Defaults observados em produção: antecedência de 30 minutos,
///     com janela de renovação entre 1 minuto e 4 horas após a emissão.
/// </summary>
public sealed class OAuthTokenOptions
{
    /// <summary>URL completa do endpoint que emite o token (ex.: <c>.../oauth/token</c>). Obrigatória antes do uso.</summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>Estratégia que apresenta a credencial ao obter o token (Basic, credenciais no corpo, etc.). Obrigatória.</summary>
    public IAuthenticationStrategy? Strategy { get; set; }

    /// <summary>Perfil de transporte usado tanto na obtenção do token quanto nas chamadas autenticadas (base, timeout, mTLS).</summary>
    public HttpClientProfile Transport { get; set; } = new();

    /// <summary>Chave sob a qual o token é armazenado. Deve ser única por integração (ex.: <c>"meu-provedor:auth:token"</c>).</summary>
    public string CacheKey { get; set; } = "aedis:http:token";

    /// <summary>Antecedência com que o token é renovado antes de expirar (o <c>RefreshAt</c> = expiração − este valor). Default 30 minutos.</summary>
    public TimeSpan ExpirationSkew { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Tempo mínimo após a emissão antes de renovar (piso da janela de renovação). Default 1 minuto.</summary>
    public TimeSpan MinimumLifetime { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Tempo máximo após a emissão antes de renovar (teto da janela de renovação). Default 4 horas.</summary>
    public TimeSpan MaximumLifetime { get; set; } = TimeSpan.FromHours(4);

    /// <summary>
    ///     TTL do lock distribuído de geração/renovação do token — tempo suficiente para buscar um token, com
    ///     auto-liberação caso o detentor falhe (evita lock preso). Default 30 segundos.
    /// </summary>
    public TimeSpan FetchLockDuration { get; set; } = TimeSpan.FromSeconds(30);
}
