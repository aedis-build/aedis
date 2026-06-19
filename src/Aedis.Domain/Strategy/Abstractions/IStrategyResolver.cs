namespace Aedis.Domain.Strategy.Abstractions;

/// <summary>
///     Resolve e executa a estratégia apropriada para um contexto, escondendo do chamador a forma de seleção
///     (O(1) por chave ou O(n) via <c>CanHandle</c>). Injete esta abstração onde precisar despachar para a
///     estratégia certa sem conhecer suas implementações.
/// </summary>
/// <typeparam name="TContext">Tipo do contexto a resolver e processar.</typeparam>
public interface IStrategyResolver<TContext>
{
    /// <summary>Seleciona a estratégia adequada ao contexto e a executa.</summary>
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
}