namespace Aedis.Security.Web.Options;

/// <summary>
///     Configura como a aplicação interpreta os cabeçalhos <c>X-Forwarded-*</c> de um proxy reverso/ingress,
///     restaurando o IP e o esquema (http/https) reais do cliente. Necessário para que rate limiting,
///     HTTPS-redirect e logs vejam a origem correta. Por default confia em qualquer proxy à frente (cenário
///     de ingress em cluster); restrinja informando <see cref="KnownProxies" />/<see cref="KnownNetworks" />
///     em ambientes onde a borda não é totalmente confiável.
/// </summary>
/// <remarks>
///     Relaciona-se a OWASP A05 e à exatidão de controles dependentes de IP. Confiar cegamente em
///     <c>X-Forwarded-For</c> de uma origem não confiável permitiria spoofing de IP — daí a opção de fixar
///     proxies/redes conhecidos.
/// </remarks>
public sealed class ForwardedHeadersHardeningOptions
{
    /// <summary>Liga ou desliga o processamento de cabeçalhos encaminhados. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Quando <c>true</c> (default), confia em qualquer proxy (limpa as listas de proxies/redes conhecidos).
    ///     Apropriado quando a aplicação só é acessível através de um ingress controlado. Defina <c>false</c>
    ///     para restringir aos endereços de <see cref="KnownProxies" />/<see cref="KnownNetworks" />.
    /// </summary>
    public bool TrustAllProxies { get; set; } = true;

    /// <summary>Endereços IP de proxies confiáveis (usado quando <see cref="TrustAllProxies" /> é <c>false</c>).</summary>
    public IList<string> KnownProxies { get; } = new List<string>();

    /// <summary>
    ///     Redes confiáveis em notação CIDR (ex.: <c>10.0.0.0/8</c>), usadas quando
    ///     <see cref="TrustAllProxies" /> é <c>false</c>.
    /// </summary>
    public IList<string> KnownNetworks { get; } = new List<string>();
}
