namespace Aedis.Security.Web.Options;

/// <summary>
///     Configura a postura de TLS: redirecionamento para HTTPS e o cabeçalho <c>Strict-Transport-Security</c>
///     (HSTS). Secure-by-default — ambos ligados. Quando o TLS é terminado num ingress/proxy à frente
///     (cenário comum em Kubernetes), desligue <see cref="EnableHttpsRedirection" /> para evitar duplo
///     redirecionamento e mantenha <see cref="EnableHsts" /> conforme a borda já não reenvie o cabeçalho.
/// </summary>
/// <remarks>
///     Cobre OWASP A02 (Cryptographic Failures) e mitiga MITRE ATT&amp;CK T1557 (adversary-in-the-middle) e
///     T1040 (sniffing). HSTS não é emitido em requisições HTTP simples nem normalmente em ambiente de
///     desenvolvimento.
/// </remarks>
public sealed class HttpsOptions
{
    /// <summary>Quando <c>true</c> (default), redireciona requisições HTTP para HTTPS via <c>UseHttpsRedirection</c>.</summary>
    public bool EnableHttpsRedirection { get; set; } = true;

    /// <summary>Quando <c>true</c> (default), emite o cabeçalho HSTS via <c>UseHsts</c> nas respostas HTTPS.</summary>
    public bool EnableHsts { get; set; } = true;

    /// <summary>Validade do HSTS em dias (<c>max-age</c>). Default 365.</summary>
    public int HstsMaxAgeDays { get; set; } = 365;

    /// <summary>Quando <c>true</c> (default), inclui <c>includeSubDomains</c> no HSTS.</summary>
    public bool HstsIncludeSubDomains { get; set; } = true;

    /// <summary>
    ///     Quando <c>true</c>, adiciona a diretiva <c>preload</c> ao HSTS. Default <c>false</c> — só ative se
    ///     for submeter o domínio à lista de preload dos navegadores (compromisso difícil de reverter).
    /// </summary>
    public bool HstsPreload { get; set; }
}
