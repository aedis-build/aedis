namespace Aedis.AI.Abstractions;

/// <summary>
///     Evento relevante de uma sessão, emitido por <see cref="IAgentEngine.SendAndFollowAsync" />: uma
///     <see cref="AgentMessage" />, uma <see cref="ToolCall" /> ou o <see cref="SessionStopped" /> final. Eventos do
///     provedor sem uso para a aplicação (telemetria, spans) são descartados pelo motor.
/// </summary>
/// <param name="Id">Id do evento no provedor; único na sessão.</param>
public abstract record AgentEvent(string Id);
