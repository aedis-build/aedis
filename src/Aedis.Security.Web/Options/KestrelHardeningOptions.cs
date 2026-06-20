namespace Aedis.Security.Web.Options;

/// <summary>
///     Configura o endurecimento do servidor Kestrel: remoção do cabeçalho <c>Server</c>, bloqueio de I/O
///     síncrono e limites de tamanho/tempo de requisição que contêm payloads abusivos e ataques de
///     slow-loris. Aplicado no bootstrap via <c>ConfigureAedisKestrelHardening</c>. Secure-by-default.
/// </summary>
/// <remarks>
///     Cobre OWASP A05 e mitiga MITRE ATT&amp;CK T1499 (DoS por exaustão de recurso). A remoção do header
///     <c>Server</c> reduz a superfície de fingerprinting da stack.
/// </remarks>
public sealed class KestrelHardeningOptions
{
    /// <summary>Liga ou desliga todo o endurecimento do Kestrel. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Quando <c>true</c> (default), remove o cabeçalho <c>Server</c> das respostas (anti-fingerprinting).</summary>
    public bool RemoveServerHeader { get; set; } = true;

    /// <summary>Quando <c>true</c> (default), proíbe I/O síncrono nos streams de requisição/resposta.</summary>
    public bool DisallowSynchronousIO { get; set; } = true;

    /// <summary>Tamanho máximo do corpo da requisição, em bytes. Default 30 MB. <c>null</c> remove o limite.</summary>
    public long? MaxRequestBodySizeBytes { get; set; } = 30L * 1024 * 1024;

    /// <summary>Tempo máximo de inatividade da conexão (keep-alive). Default 30s.</summary>
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Tempo máximo para receber os cabeçalhos da requisição (mitiga slow-loris). Default 30s.</summary>
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Número máximo de cabeçalhos por requisição. Default 50.</summary>
    public int MaxRequestHeaderCount { get; set; } = 50;

    /// <summary>Tamanho máximo total dos cabeçalhos, em bytes. Default 32 KB.</summary>
    public int MaxRequestHeadersTotalSize { get; set; } = 32 * 1024;
}
