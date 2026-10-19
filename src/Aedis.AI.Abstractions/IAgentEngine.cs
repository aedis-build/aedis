namespace Aedis.AI.Abstractions;

/// <summary>
///     Motor que executa agentes de IA em sessões gerenciadas pelo provedor. É a costura entre a aplicação e a
///     plataforma de agentes: o código de aplicação depende só deste contrato e o provedor (ex.:
///     <c>Aedis.AI.ManagedAgents</c>) é escolhido na composition root por <i>package swap</i>.
/// </summary>
/// <remarks>
///     Ferramentas próprias chegam como <see cref="ToolCall" />: a entrada vem do modelo e é dado não confiável,
///     leia-a com <see cref="ToolArguments" />. Toda sessão nasce com teto de gasto
///     (<see cref="NewSession.BudgetCents" />).
/// </remarks>
public interface IAgentEngine
{
    /// <summary>Cria a sessão de um agente e devolve o id dela, usado nas demais chamadas.</summary>
    Task<string> CreateSessionAsync(NewSession session, CancellationToken ct);

    /// <summary>
    ///     Envia à sessão as respostas de ferramentas, a mensagem do usuário e o contexto de sistema (todos
    ///     opcionais) e acompanha os eventos até a sessão parar. O último evento é sempre um
    ///     <see cref="SessionStopped" />.
    /// </summary>
    /// <param name="sessionId">Sessão criada por <see cref="CreateSessionAsync" />.</param>
    /// <param name="system">Contexto de sistema; costuma ir só no primeiro envio.</param>
    /// <param name="message">Mensagem do usuário.</param>
    /// <param name="toolResults">Respostas às ferramentas que a sessão está esperando.</param>
    /// <param name="ct">Cancela o envio e o acompanhamento.</param>
    IAsyncEnumerable<AgentEvent> SendAndFollowAsync(string sessionId, string? system, string? message, IReadOnlyList<ToolResult> toolResults, CancellationToken ct);

    /// <summary>
    ///     Envia respostas de ferramentas sem acompanhar a sessão. Útil quando ela ainda espera outras respostas
    ///     e o acompanhamento será retomado por um <see cref="SendAndFollowAsync" /> posterior.
    /// </summary>
    Task SendToolResultsAsync(string sessionId, IReadOnlyList<ToolResult> toolResults, CancellationToken ct);

    /// <summary>Gasto acumulado da sessão, em centavos de dólar; 0 quando o provedor ainda não informa.</summary>
    Task<long> GetCostCentsAsync(string sessionId, CancellationToken ct);

    /// <summary>Cria uma memória persistente (montável em sessões por <see cref="MountedMemory" />) e devolve o id.</summary>
    Task<string> CreateMemoryStoreAsync(string name, string description, CancellationToken ct);

    /// <summary>
    ///     Escreve um arquivo numa memória persistente. É o caminho da plataforma para alimentar memórias que os
    ///     agentes montam só para leitura (<see cref="MountedMemory.ReadOnly" />).
    /// </summary>
    Task WriteMemoryAsync(string memoryStoreId, string path, string content, CancellationToken ct);
}
