using Microsoft.Extensions.Logging;
using Aedis.Domain.Strategy.Abstractions;

namespace Aedis.Domain.Strategy;

/// <summary>
///     Resolver O(1) para estratégias com chave direta.
///     Usa dicionário para lookup eficiente baseado na chave.
/// </summary>
/// <typeparam name="TKey">Tipo da chave (deve ser comparável e não nulo)</typeparam>
/// <typeparam name="TContext">Tipo do contexto</typeparam>
/// <typeparam name="TStrategy">Tipo específico da estratégia (deve implementar IKeyedStrategy)</typeparam>
public class KeyedStrategyResolver<TKey, TContext, TStrategy> : IStrategyResolver<TContext>
    where TKey : notnull
    where TStrategy : class, IKeyedStrategy<TKey, TContext>
{
    private readonly Func<TContext, TKey> _keySelector;
    private readonly ILogger<KeyedStrategyResolver<TKey, TContext, TStrategy>>? _logger;
    private readonly Dictionary<TKey, TStrategy> _strategies;

    public KeyedStrategyResolver(
        IEnumerable<TStrategy> strategies,
        Func<TContext, TKey> keySelector,
        ILogger<KeyedStrategyResolver<TKey, TContext, TStrategy>>? logger = null) {
        _strategies = strategies.ToDictionary(s => s.Key);
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _logger = logger;
    }

    public async Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default) {
        var key = _keySelector(context);

        if (!_strategies.TryGetValue(key, out var strategy)) {
            _logger?.LogError("No strategy found for key {Key}", key);
            throw new NotSupportedException(
                $"No strategy registered for key '{key}'. Available keys: {string.Join(", ", _strategies.Keys)}");
        }

        _logger?.LogTrace("Resolved strategy {Strategy} for key {Key}", strategy.GetType().Name, key);
        await strategy.ExecuteAsync(context, cancellationToken);
    }

    /// <summary>
    ///     Obtém a estratégia para a chave especificada.
    ///     Retorna o tipo específico TStrategy, eliminando a necessidade de casts.
    /// </summary>
    public TStrategy GetStrategy(TKey key) {
        if (!_strategies.TryGetValue(key, out var strategy))
            throw new NotSupportedException(
                $"No strategy registered for key '{key}'. Available keys: {string.Join(", ", _strategies.Keys)}");
        return strategy;
    }
}

/// <summary>
///     Classe de conveniência para manter compatibilidade com código existente.
///     Usa IKeyedStrategy como tipo de estratégia.
/// </summary>
/// <typeparam name="TKey">Tipo da chave (deve ser comparável e não nulo)</typeparam>
/// <typeparam name="TContext">Tipo do contexto</typeparam>
public class
    KeyedStrategyResolver<TKey, TContext> : KeyedStrategyResolver<TKey, TContext, IKeyedStrategy<TKey, TContext>>
    where TKey : notnull
{
    public KeyedStrategyResolver(
        IEnumerable<IKeyedStrategy<TKey, TContext>> strategies,
        Func<TContext, TKey> keySelector,
        ILogger<KeyedStrategyResolver<TKey, TContext, IKeyedStrategy<TKey, TContext>>>? logger = null)
        : base(strategies, keySelector, logger) { }
}