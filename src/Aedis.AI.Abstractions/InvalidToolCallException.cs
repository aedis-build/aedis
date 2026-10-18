namespace Aedis.AI.Abstractions;

/// <summary>
///     Argumentos de ferramenta própria inválidos. Não é falha da aplicação: capture e devolva a mensagem à sessão
///     como <see cref="ToolResult" /> com <see cref="ToolResult.IsError" />, para o agente corrigir a chamada.
/// </summary>
public sealed class InvalidToolCallException(string message) : Exception(message);
