using System.Text.Json;

namespace Aedis.AI.Abstractions;

/// <summary>
///     O agente chamou uma ferramenta própria da aplicação; a sessão espera a resposta (<see cref="ToolResult" />).
/// </summary>
/// <remarks>
///     <see cref="Input" /> é gerado pelo modelo e cruza uma fronteira de confiança: pode faltar campo, vir com tipo
///     errado ou trazer conteúdo induzido por prompt injection. Leia com <see cref="ToolArguments" /> e autorize a
///     ação pelo contexto da aplicação, nunca pelo que o modelo afirma.
/// </remarks>
/// <param name="Id">Id do evento; é o <see cref="ToolResult.ToolUseId" /> da resposta.</param>
/// <param name="Name">Nome da ferramenta.</param>
/// <param name="Input">Argumentos, como o modelo mandou.</param>
public sealed record ToolCall(string Id, string Name, JsonElement Input) : AgentEvent(Id);
