namespace Aedis.AI.Abstractions;

/// <summary>Texto que o agente escreveu, com as partes de texto da mensagem já concatenadas.</summary>
/// <param name="Id">Id do evento.</param>
/// <param name="Text">Texto da mensagem.</param>
public sealed record AgentMessage(string Id, string Text) : AgentEvent(Id);
