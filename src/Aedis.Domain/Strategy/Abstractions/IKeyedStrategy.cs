namespace Aedis.Domain.Strategy.Abstractions;

/// <summary>
///     Estratégia que pode ser identificada por uma chave direta (enum, string, etc).
///     Permite seleção O(1) via dicionário ao invés de O(n) com CanHandle.
/// </summary>
/// <typeparam name="TKey">Tipo da chave (deve ser comparável e não nulo)</typeparam>
/// <typeparam name="TContext">Tipo do contexto</typeparam>
public interface IKeyedStrategy<TKey, in TContext> : IStrategy<TContext>
    where TKey : notnull
{
    /// <summary>
    ///     Chave única que identifica esta estratégia.
    /// </summary>
    TKey Key { get; }
}