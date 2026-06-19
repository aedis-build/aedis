namespace Aedis.Cache.Abstractions;

/// <summary>
///     Fábrica de <see cref="IExecutionCacheContext" />. Registrada como singleton; cada chamada a
///     <see cref="Create" /> devolve um contexto novo, com seu próprio estado de commit por execução.
/// </summary>
public interface IExecutionCacheContextFactory
{
    /// <summary>Cria um contexto de execução novo, independente, para um único ciclo de processamento.</summary>
    IExecutionCacheContext Create();
}