using Aedis.Domain.Strategy.Abstractions;

namespace Aedis.Domain.Strategy;

/// <summary>
///     Resolver de estratégias por contexto, aberto a herança: executa a primeira estratégia cujo
///     <see cref="IStrategy{TContext}.CanHandle" /> aceita o contexto e expõe <see cref="Strategies" /> para
///     resolvers especializados (ex.: estratégias de nomes do banco). Para escolha por chave use
///     <see cref="KeyedStrategyResolver{TKey,TContext}" />.
/// </summary>
/// <typeparam name="TContext">Tipo do contexto avaliado pelas estratégias.</typeparam>
/// <typeparam name="TStrategy">Tipo das estratégias registradas.</typeparam>
public class StrategyResolver<TContext, TStrategy> : IStrategyResolver<TContext>
    where TStrategy : class, IStrategy<TContext>
{
    private readonly ContextStrategyResolver<TContext, TStrategy> _resolver;

    /// <summary>Estratégias registradas, na ordem de avaliação.</summary>
    protected readonly IEnumerable<TStrategy> Strategies;

    /// <summary>Cria o resolver sobre as estratégias.</summary>
    public StrategyResolver(IEnumerable<TStrategy> strategies) {
        Strategies = strategies?.ToList() ?? throw new ArgumentNullException(nameof(strategies));
        _resolver = new ContextStrategyResolver<TContext, TStrategy>(Strategies);
    }

    /// <inheritdoc />
    public virtual Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default) =>
        _resolver.ExecuteAsync(context, cancellationToken);

    /// <summary>Primeira estratégia que trata o contexto; <see cref="NotSupportedException" /> quando nenhuma trata.</summary>
    public TStrategy GetStrategy(TContext context) =>
        _resolver.GetStrategy(context)
        ?? throw new NotSupportedException(
            $"No strategy found for context type '{typeof(TContext).Name}'. Available strategies: {string.Join(", ", Strategies.Select(s => s.GetType().Name))}");

    /// <summary>Estratégia com a chave informada (entre as que implementam <see cref="IKeyedStrategy{TKey,TContext}" />).</summary>
    public TStrategy GetStrategy<TKey>(TKey key) where TKey : notnull =>
        Strategies.FirstOrDefault(s => s is IKeyedStrategy<TKey, TContext> keyed && keyed.Key.Equals(key))
        ?? throw new NotSupportedException(
            $"No strategy found for key '{key}'. Available strategies: {string.Join(", ", Strategies.Select(s => s.GetType().Name))}");
}

/// <summary>Atalho de <see cref="StrategyResolver{TContext,TStrategy}" /> para estratégias <see cref="IStrategy{TContext}" />.</summary>
/// <typeparam name="TContext">Tipo do contexto avaliado pelas estratégias.</typeparam>
public class StrategyResolver<TContext> : StrategyResolver<TContext, IStrategy<TContext>>
{
    /// <summary>Cria o resolver sobre as estratégias.</summary>
    public StrategyResolver(IEnumerable<IStrategy<TContext>> strategies) : base(strategies) { }
}
