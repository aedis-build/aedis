namespace Aedis.Security.Web.Options;

/// <summary>
///     Configura a proteção de cabeçalho <c>Host</c> — recusa requisições cujo Host não esteja na lista de
///     permitidos, defendendo contra host-header injection, cache poisoning e password-reset poisoning.
///     Substitui o <c>HostFilteringMiddleware</c> nativo para liberar acesso legítimo por IP direto e por
///     DNS interno de cluster (ex.: Kubernetes), o que o filtro padrão bloquearia. Secure-by-default ligado.
/// </summary>
/// <remarks>
///     Cobre OWASP A05 e mitiga MITRE ATT&amp;CK T1190. Por padrão aceita <c>localhost</c>, IPs literais e
///     hosts <c>*.svc.cluster.local</c>; adicione domínios públicos em <see cref="AllowedHosts" /> (suporta
///     curinga de sufixo, ex.: <c>*.exemplo.com</c>). Requisições a <see cref="BypassPathPrefixes" /> (probes
///     de saúde/métricas) não são checadas.
/// </remarks>
public sealed class HostHeaderProtectionOptions
{
    /// <summary>Liga ou desliga a proteção de Host. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Hosts públicos permitidos além dos internos sempre aceitos (localhost, IPs, <c>*.svc.cluster.local</c>).
    ///     Aceita curinga de sufixo: <c>*.exemplo.com</c> casa qualquer subdomínio.
    /// </summary>
    public IList<string> AllowedHosts { get; } = new List<string>();

    /// <summary>
    ///     Quando <c>true</c> (default), aceita Host que seja um endereço IP literal (acesso direto por IP,
    ///     comum em probes e malha de serviço).
    /// </summary>
    public bool AllowDirectIpAccess { get; set; } = true;

    /// <summary>
    ///     Prefixos de caminho isentos da checagem de Host — tipicamente os endpoints de saúde e métricas,
    ///     acessados internamente por IP. Default <c>/health</c> e <c>/metrics</c>.
    /// </summary>
    public IList<string> BypassPathPrefixes { get; } = new List<string> { "/health", "/metrics" };
}
