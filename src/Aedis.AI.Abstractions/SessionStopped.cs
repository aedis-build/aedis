namespace Aedis.AI.Abstractions;

/// <summary>A sessão parou. É sempre o último evento de um <see cref="IAgentEngine.SendAndFollowAsync" />.</summary>
/// <param name="Id">Id do evento; vazio quando a parada foi detectada pelo motor (ex.: stream encerrado).</param>
/// <param name="Reason">Motivo, um dos valores de <see cref="StopReasons" />.</param>
/// <param name="Detail">Mensagem de erro, quando houver.</param>
public sealed record SessionStopped(string Id, string Reason, string? Detail = null) : AgentEvent(Id);
