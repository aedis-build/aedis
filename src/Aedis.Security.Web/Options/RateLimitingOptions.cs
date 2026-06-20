namespace Aedis.Security.Web.Options;

/// <summary>
///     Configura o rate limiting global (janela fixa) particionado por cliente — usuário autenticado quando
///     há identidade, senão o IP de origem. Secure-by-default: ligado, protegendo contra força bruta e abuso
///     de endpoint. Ajuste os limites ao seu tráfego ou desligue quando o controle vive no ingress/API gateway.
/// </summary>
/// <remarks>
///     Cobre OWASP A04/A05 e mitiga MITRE ATT&amp;CK T1110 (brute force) e T1499 (endpoint DoS). Requisições
///     além do limite recebem <see cref="RejectionStatusCode" /> (429 por padrão).
/// </remarks>
public sealed class RateLimitingOptions
{
    /// <summary>Liga ou desliga o rate limiting global. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Número de requisições permitidas por janela, por partição (cliente). Default 100.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Duração da janela fixa de contagem. Default 1 minuto.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Quantidade de requisições que aguardam em fila quando o limite é atingido (em vez de rejeitar na
    ///     hora). Default 0 — rejeita imediatamente.
    /// </summary>
    public int QueueLimit { get; set; }

    /// <summary>Status HTTP retornado quando o limite é excedido. Default 429 (Too Many Requests).</summary>
    public int RejectionStatusCode { get; set; } = 429;
}
