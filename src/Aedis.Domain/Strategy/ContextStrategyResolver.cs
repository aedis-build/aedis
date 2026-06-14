using Microsoft.Extensions.Logging;

namespace Aedis.Domain.Strategy;

/// <summary>
///     Resolver O(n) que usa CanHandle para selecionar a estratégia apropriada.
///     Compatível com o comportamento padrão do Strategy Pattern.
/// </summary>
/// <typeparam name="TContext">Tipo do contexto</typeparam>
/// <typeparam name="TStrategy">Tipo específico da estratégia (deve implementar IStrategy)</typeparam>
public class ContextStrategyResolver<TContext, TStrategy> : IStrategyResolver<TContext>
    where TStrategy : class, IStrategy<TContext>
{
    private readonly ILogger<ContextStrategyResolver<TContext, TStrategy>>? _logger;
    private readonly IEnumerable<TStrategy> _strategies;

    public ContextStrategyResolver(
        IEnumerable<TStrategy> strategies,
        ILogger<ContextStrategyResolver<TContext, TStrategy>>? logger = null) {
        _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
        _logger = logger;
    }

    public async Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default) {
        var strategy = _strategies.FirstOrDefault(s => s.CanHandle(context));

        if (strategy == null) {
            _logger?.LogError("No strategy found for context type {ContextType}", typeof(TContext).Name);
            throw new NotSupportedException(
                $"No strategy found for context type '{typeof(TContext).Name}'. Available strategies: {string.Join(", ", _strategies.Select(s => s.GetType().Name))}");
        }

        _logger?.LogTrace("Resolved strategy {Strategy} for context type {ContextType}",
            strategy.GetType().Name, typeof(TContext).Name);

        await strategy.ExecuteAsync(context, cancellationToken);
    }

    /// <summary>
    ///     Obtém a estratégia para o contexto especificado.
    ///     Retorna o tipo específico TStrategy, eliminando a necessidade de casts.
    /// </summary>
    public TStrategy? GetStrategy(TContext context) {
        return _strategies.FirstOrDefault(s => s.CanHandle(context));
    }
}

/// <summary>
///     Classe de conveniência para manter compatibilidade com código existente.
///     Usa IStrategy como tipo de estratégia.
/// </summary>
/// <typeparam name="TContext">Tipo do contexto</typeparam>
public class ContextStrategyResolver<TContext> : ContextStrategyResolver<TContext, IStrategy<TContext>>
{
    public ContextStrategyResolver(
        IEnumerable<IStrategy<TContext>> strategies,
        ILogger<ContextStrategyResolver<TContext, IStrategy<TContext>>>? logger = null)
        : base(strategies, logger) { }
}