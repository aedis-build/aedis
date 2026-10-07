namespace Aedis.Security.Web.Options;

/// <summary>
///     CORS por lista explícita de origens. Desligado por padrão — same-origin é a postura segura. Quando
///     ligado, exige <see cref="AllowedOrigins" /> (não há fallback para qualquer origem) e recusa combinar
///     <see cref="AllowCredentials" /> com <c>*</c>, combinação que o navegador também rejeita. Suporta curinga
///     de subdomínio (<c>https://*.exemplo.com</c>). <c>QUERY</c> entra nos métodos padrão para as consultas com
///     corpo. A validação roda na subida do host (fail-closed).
/// </summary>
public sealed class CorsOptions
{
    /// <summary>Nome da política CORS registrada no ASP.NET Core.</summary>
    public const string PolicyName = "AedisCors";

    /// <summary>Liga o CORS. Padrão: desligado (same-origin).</summary>
    public bool Enabled { get; set; }

    /// <summary>Origens permitidas, obrigatórias quando ligado. <c>*</c> é opt-in consciente e incompatível com credenciais.</summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>Métodos permitidos. Inclui <c>QUERY</c> por padrão.</summary>
    public List<string> AllowedMethods { get; set; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "QUERY"];

    /// <summary>Cabeçalhos de requisição permitidos.</summary>
    public List<string> AllowedHeaders { get; set; } = ["Content-Type", "Authorization", "Accept", "Idempotency-Key"];

    /// <summary>Cabeçalhos de resposta expostos ao chamador.</summary>
    public List<string> ExposedHeaders { get; set; } = ["Location", "Content-Disposition"];

    /// <summary>Permite credenciais (cookies, Authorization) em requisições cross-origin. Padrão: não.</summary>
    public bool AllowCredentials { get; set; }

    /// <summary>Tempo que o navegador pode cachear a resposta de preflight. Padrão: 1 hora.</summary>
    public TimeSpan PreflightMaxAge { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Verdadeiro quando <see cref="AllowedOrigins" /> contém o curinga <c>*</c>.</summary>
    public bool AllowsAnyOrigin => AllowedOrigins.Any(origin => origin == "*");
}
