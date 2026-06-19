using System.ComponentModel.DataAnnotations;

namespace Aedis.Cache.Redis;

/// <summary>
///     Opções de conexão do provider Redis do Aedis. Lidas da seção <c>REDIS</c> da configuração.
///     Suporta instância única ou Sentinel (via <see cref="SentinelMasterName" />).
/// </summary>
public sealed class RedisCacheOptions
{
    /// <summary>Nome da seção de configuração de onde estas opções são lidas (<c>REDIS</c>).</summary>
    public const string SectionName = "REDIS";

    /// <summary>Endpoint do Redis no formato <c>host:porta</c>.</summary>
    [Required]
    public required string EndPoint { get; set; }

    /// <summary>Usuário (ACL do Redis 6+). Opcional.</summary>
    public string? User { get; set; }

    /// <summary>Senha de autenticação.</summary>
    public required string Password { get; set; }

    /// <summary>Habilita TLS na conexão.</summary>
    [Required]
    public required bool UseSsl { get; set; }

    /// <summary>
    ///     Nome do master quando usando Redis Sentinel. Quando definido, a conexão descobre o master via
    ///     Sentinel; quando vazio, conecta direto (Standalone ou Cluster — o driver detecta o cluster).
    /// </summary>
    public string? SentinelMasterName { get; set; }

    /// <summary>Usuário para autenticar nos nós Sentinel (ACL). Opcional.</summary>
    public string? SentinelUser { get; set; }

    /// <summary>Senha para autenticar nos nós Sentinel. Opcional.</summary>
    public string? SentinelPassword { get; set; }

    /// <summary>
    ///     Identidade desta instância para eleição de líder (valor gravado no lock). Quando nulo, usa
    ///     <see cref="Environment.MachineName" /> — em Kubernetes, único por pod. Defina explicitamente
    ///     quando precisar de uma identidade estável independente do host.
    /// </summary>
    public string? InstanceId { get; set; }
}
