namespace Aedis.Http.Abstractions.Authentication;

/// <summary>
///     Configura um provedor de token OAuth2 <c>client_credentials</c>: endpoint, estratégia de credencial,
///     perfil de transporte (incl. mTLS), chave de cache e a política de validade (skew e limites). Os
///     defaults de validade replicam o padrão observado nas integrações de produção: margem de segurança de
///     30 minutos, com piso de 1 minuto e teto de 4 horas sobre o <c>expires_in</c> recebido.
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

    /// <summary>Margem de segurança subtraída da validade do token para renová-lo antes de expirar. Default 30 minutos.</summary>
    public TimeSpan ExpirationSkew { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Validade mínima aplicada ao token cacheado (piso do TTL). Default 1 minuto.</summary>
    public TimeSpan MinimumLifetime { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Validade máxima aplicada ao token cacheado (teto do TTL). Default 4 horas.</summary>
    public TimeSpan MaximumLifetime { get; set; } = TimeSpan.FromHours(4);
}
