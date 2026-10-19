namespace Aedis.AI.Abstractions;

/// <summary>Parâmetros para abrir a sessão de um agente (<see cref="IAgentEngine.CreateSessionAsync" />).</summary>
/// <param name="AgentId">Agente configurado no provedor.</param>
/// <param name="EnvironmentId">Ambiente de execução do provedor.</param>
/// <param name="Memories">Memórias montadas na sessão (o provedor limita a quantidade; no Managed Agents, 8).</param>
/// <param name="Skills">Ids de skills que substituem as do agente nesta sessão; vazio usa as do agente.</param>
/// <param name="BudgetCents">
///     Teto de gasto da sessão, em centavos de dólar; obrigatório e positivo. Ao atingi-lo a sessão para com
///     <see cref="StopReasons.BudgetReached" /> — é o limite de custo por sessão, não um aviso.
/// </param>
/// <param name="Title">Título da sessão, exibido no console do provedor.</param>
/// <param name="Metadata">Pares chave/valor para rastreio (o provedor limita a quantidade; no Managed Agents, 8).</param>
public sealed record NewSession(
    string AgentId,
    string EnvironmentId,
    IReadOnlyList<MountedMemory> Memories,
    IReadOnlyList<string> Skills,
    long BudgetCents,
    string Title,
    IReadOnlyDictionary<string, string> Metadata);
