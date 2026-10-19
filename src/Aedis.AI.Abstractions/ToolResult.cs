namespace Aedis.AI.Abstractions;

/// <summary>Resultado de uma ferramenta própria, devolvido à sessão que a chamou.</summary>
/// <param name="ToolUseId">Id da <see cref="ToolCall" /> respondida.</param>
/// <param name="Text">Resultado em texto.</param>
/// <param name="IsError">Se a ferramenta falhou; o agente recebe o texto como erro e pode corrigir a chamada.</param>
public sealed record ToolResult(string ToolUseId, string Text, bool IsError = false);
