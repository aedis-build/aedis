namespace Aedis.Security.Web.Options;

/// <summary>
///     Configura os cabeçalhos de segurança de resposta (defesa contra clickjacking, MIME-sniffing,
///     vazamento de referer e abuso de APIs do navegador). Os defaults são restritivos e adequados a
///     uma API REST sem UI; relaxe apenas o necessário quando servir conteúdo HTML/SPA. Aplicados pelo
///     <c>SecurityHeadersMiddleware</c> em toda resposta.
/// </summary>
/// <remarks>
///     Cobre OWASP A05 (Security Misconfiguration) e mitiga MITRE ATT&amp;CK T1185/T1539. O cabeçalho
///     <c>Strict-Transport-Security</c> (HSTS) NÃO é definido aqui — vive em <see cref="HttpsOptions" />,
///     por ser parte da postura de TLS.
/// </remarks>
public sealed class SecurityHeadersOptions
{
    /// <summary>Liga ou desliga a aplicação de todos os cabeçalhos de segurança. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Valor de <c>Content-Security-Policy</c>. O default bloqueia todo carregamento e enquadramento
    ///     (<c>default-src 'none'; frame-ancestors 'none'</c>), ideal para APIs. Defina <c>null</c> ou vazio
    ///     para omitir o cabeçalho.
    /// </summary>
    public string? ContentSecurityPolicy { get; set; } = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

    /// <summary>
    ///     Quando <c>true</c> (default), emite <c>X-Content-Type-Options: nosniff</c>, impedindo o navegador
    ///     de adivinhar o content-type (anti MIME-sniffing).
    /// </summary>
    public bool ContentTypeNoSniff { get; set; } = true;

    /// <summary>
    ///     Valor de <c>X-Frame-Options</c> (anti-clickjacking). Default <c>DENY</c>. Use <c>null</c>/vazio para
    ///     omitir — prefira controlar enquadramento via <c>frame-ancestors</c> na CSP em navegadores modernos.
    /// </summary>
    public string? FrameOptions { get; set; } = "DENY";

    /// <summary>Valor de <c>Referrer-Policy</c>. Default <c>no-referrer</c> (não vaza a URL de origem).</summary>
    public string? ReferrerPolicy { get; set; } = "no-referrer";

    /// <summary>
    ///     Valor de <c>Permissions-Policy</c>. Default desliga as APIs sensíveis do navegador
    ///     (geolocalização, câmera, microfone). Use <c>null</c>/vazio para omitir.
    /// </summary>
    public string? PermissionsPolicy { get; set; } = "geolocation=(), camera=(), microphone=()";

    /// <summary>Valor de <c>Cross-Origin-Opener-Policy</c>. Default <c>same-origin</c>. <c>null</c>/vazio omite.</summary>
    public string? CrossOriginOpenerPolicy { get; set; } = "same-origin";

    /// <summary>Valor de <c>Cross-Origin-Resource-Policy</c>. Default <c>same-origin</c>. <c>null</c>/vazio omite.</summary>
    public string? CrossOriginResourcePolicy { get; set; } = "same-origin";

    /// <summary>
    ///     Cabeçalhos extras (ou overrides) a emitir, aplicados após os acima. Use para necessidades
    ///     específicas sem precisar de middleware próprio.
    /// </summary>
    public IDictionary<string, string> CustomHeaders { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
