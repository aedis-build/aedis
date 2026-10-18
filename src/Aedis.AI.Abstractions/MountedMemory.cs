namespace Aedis.AI.Abstractions;

/// <summary>Memória persistente montada numa sessão (<see cref="NewSession.Memories" />).</summary>
/// <param name="MemoryStoreId">Memória criada por <see cref="IAgentEngine.CreateMemoryStoreAsync" />.</param>
/// <param name="ReadOnly">
///     <c>true</c> monta só para leitura: o agente consulta, mas não altera. Use para conteúdo curado pela
///     plataforma (diretrizes, políticas), que só muda por <see cref="IAgentEngine.WriteMemoryAsync" />; reserve a
///     escrita para memórias que o próprio agente mantém.
/// </param>
/// <param name="Instructions">Como o agente deve usar a memória (no Managed Agents, até 4.096 caracteres).</param>
public sealed record MountedMemory(string MemoryStoreId, bool ReadOnly, string Instructions);
