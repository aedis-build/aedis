using System.Security.Cryptography.X509Certificates;

namespace Aedis.Http.Abstractions;

/// <summary>
///     Perfil de transporte de um cliente HTTP: endereço base, timeout, certificados de cliente (mTLS) e
///     cabeçalhos padrão. É a unidade que um <see cref="IAedisHttpClientFactory" /> recebe para materializar
///     um <see cref="IAedisHttpClient" /> — mantendo o código de integração agnóstico de como o provider
///     anexa certificados ou monta o handler de transporte.
/// </summary>
public sealed class HttpClientProfile
{
    /// <summary>Endereço base ao qual as URLs relativas das requisições são resolvidas; <c>null</c> exige URLs absolutas.</summary>
    public string? BaseAddress { get; init; }

    /// <summary>Timeout padrão das requisições deste cliente. Default 30s.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Certificados de cliente para mTLS. Quando presentes, o provider os anexa ao handler de transporte
    ///     (autenticação mútua TLS). Vazio para conexões sem certificado de cliente.
    /// </summary>
    public IReadOnlyCollection<X509Certificate2> ClientCertificates { get; init; } = [];

    /// <summary>Cabeçalhos aplicados a toda requisição deste cliente (ex.: <c>User-Agent</c>, chaves de API fixas).</summary>
    public IDictionary<string, string> DefaultHeaders { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Política anti-SSRF do transporte. Quando habilitada, recusa conexões a endereços internos
    ///     verificando o IP resolvido no momento de conectar (imune a DNS rebinding). Default desligada (opt-in).
    /// </summary>
    public SsrfPolicy Ssrf { get; init; } = new();
}
