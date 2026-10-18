namespace Aedis.Hosting.AspNetCore.ServerSentEvents;

/// <summary>
///     Limites dos streams Server-Sent Events abertos por
///     <see cref="ServerSentEventsControllerExtensions.ServerSentEvents{T}" />. Lido da seção
///     <c>ServerSentEvents</c> da configuração pelo <see cref="AedisApiHost" />.
/// </summary>
/// <example>
///     <code>
///     "ServerSentEvents": { "HeartbeatInterval": "00:00:15", "MaxConnectionDuration": "00:30:00" }
///     </code>
/// </example>
public sealed class ServerSentEventsOptions
{
    /// <summary>Seção de configuração lida pelo host.</summary>
    public const string SectionName = "ServerSentEvents";

    /// <summary>
    ///     Intervalo entre comentários de heartbeat (<c>: heartbeat</c>), que o navegador ignora mas mantêm a
    ///     conexão viva em proxies e load balancers com timeout de ociosidade (ALB: 60 s). Padrão 15 s.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     Duração máxima de uma conexão. Ao atingi-la o stream termina normalmente e o cliente reconecta com
    ///     <c>Last-Event-ID</c> — o que também reapresenta a autenticação, já que o token só é validado na
    ///     abertura. Padrão 30 min.
    /// </summary>
    public TimeSpan MaxConnectionDuration { get; set; } = TimeSpan.FromMinutes(30);
}
