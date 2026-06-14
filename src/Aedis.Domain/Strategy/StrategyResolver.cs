using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Aedis.Domain.Strategy;

/// <summary>
///     Resolver híbrido que auto-detecta se deve usar O(1) (KeyedStrategyResolver) ou O(n) (ContextStrategyResolver).
///     Se todas as estratégias implementam IKeyedStrategy e uma keySelector é fornecida, usa O(1).
///     Caso contrário, usa O(n) com CanHandle.
///     Classes podem herdar desta classe e sobrescrever ExecuteAsync para comportamento customizado.
/// </summary>
/// <typeparam name="TContext">Tipo do contexto</typeparam>
/// <typeparam name="TStrategy">Tipo específico da estratégia (deve implementar IStrategy)</typeparam>
public class StrategyResolver<TContext, TStrategy> : IStrategyResolver<TContext>
    where TStrategy : class, IStrategy<TContext>
{
    private readonly IStrategyResolver<TContext> _internalResolver;
    protected readonly IEnumerable<TStrategy> Strategies;

    /// <summary>
    ///     Construtor que auto-detecta O(1) ou O(n) baseado nas estratégias fornecidas.
    ///     Detecta automaticamente se TStrategy é IKeyedStrategy para usar KeyedStrategyResolver.
    /// </summary>
    /// <param name="strategies">Estratégias disponíveis</param>
    /// <param name="keySelector">Seletor de chave para O(1). Se null, usa O(n)</param>
    /// <param name="logger">Logger opcional</param>
    public StrategyResolver(
        IEnumerable<TStrategy> strategies,
        Func<TContext, object>? keySelector = null,
        ILogger<StrategyResolver<TContext, TStrategy>>? logger = null) {
        Strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
        var strategyList = strategies.ToList();

        var keyedInterface = typeof(TStrategy).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                                 i.GetGenericTypeDefinition() == typeof(IKeyedStrategy<,>) &&
                                 i.GetGenericArguments()[1] == typeof(TContext));

        if (keySelector != null && keyedInterface != null && strategyList.Count > 0) {
            var keyType = keyedInterface.GetGenericArguments()[0];
            _internalResolver = CreateKeyedResolverTyped(keyType, strategyList, keySelector, logger);
        }
        else {
            _internalResolver = new ContextStrategyResolver<TContext, TStrategy>(
                strategyList);
        }
    }

    public virtual Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default) {
        return _internalResolver.ExecuteAsync(context, cancellationToken);
    }

    private IStrategyResolver<TContext> CreateKeyedResolverTyped(
        Type keyType,
        List<TStrategy> strategies,
        Func<TContext, object> keySelector,
        ILogger<StrategyResolver<TContext, TStrategy>>? logger) {
        var keyedInterface = typeof(IKeyedStrategy<,>).MakeGenericType(keyType, typeof(TContext));
        var resolverType =
            typeof(KeyedStrategyResolver<,,>).MakeGenericType(keyType, typeof(TContext), typeof(TStrategy));

        var typedKeySelector = CreateTypedKeySelector(keySelector, keyType);
        var constructor = resolverType.GetConstructor(new[] {
            typeof(IEnumerable<>).MakeGenericType(typeof(TStrategy)),
            typeof(Func<,>).MakeGenericType(typeof(TContext), keyType),
            typeof(ILogger<>).MakeGenericType(resolverType)
        });

        if (constructor == null) return new ContextStrategyResolver<TContext, TStrategy>(strategies);

        object? loggerTyped = null;

        return (IStrategyResolver<TContext>)constructor.Invoke(new[] { strategies, typedKeySelector, loggerTyped })!;
    }

    private object CreateTypedKeySelector(Func<TContext, object> keySelector, Type keyType) {
        var method = typeof(StrategyResolver<TContext, TStrategy>).GetMethod(
            nameof(CreateKeySelectorHelper),
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null) throw new InvalidOperationException("Failed to create typed key selector");

        var genericMethod = method.MakeGenericMethod(keyType);
        return genericMethod.Invoke(null, new object[] { keySelector })!;
    }

    private static Func<TContext, TKey> CreateKeySelectorHelper<TKey>(Func<TContext, object> keySelector) {
        return ctx => (TKey)keySelector(ctx);
    }

    private TStrategy GetStrategyByContextKey(TContext context) {
        foreach (var strategy in Strategies) {
            var keyedInterface = strategy.GetType().GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                                     i.GetGenericTypeDefinition() == typeof(IKeyedStrategy<,>) &&
                                     i.GetGenericArguments()[1] == typeof(TContext));

            if (keyedInterface != null) {
                var keyProperty = keyedInterface.GetProperty("Key");
                if (keyProperty != null) {
                    var keyValue = keyProperty.GetValue(strategy);
                    if (keyValue != null && keyValue.Equals(context)) return strategy;
                }
            }
        }

        throw new NotSupportedException(
            $"No strategy found for context key '{context}'. Available strategies: {string.Join(", ", Strategies.Select(s => s.GetType().Name))}");
    }

    /// <summary>
    ///     Obtém a estratégia para o contexto especificado.
    ///     Retorna o tipo específico TStrategy, eliminando a necessidade de casts.
    ///     Quando TContext é a chave do IKeyedStrategy, usa O(1) lookup.
    ///     Sempre lança exceção quando não encontra a estratégia.
    /// </summary>
    public TStrategy GetStrategy(TContext context) {
        var keyedInterface = typeof(TStrategy).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                                 i.GetGenericTypeDefinition() == typeof(IKeyedStrategy<,>) &&
                                 i.GetGenericArguments()[1] == typeof(TContext));

        if (keyedInterface != null) {
            var keyType = keyedInterface.GetGenericArguments()[0];
            if (keyType == typeof(TContext) && context != null) return GetStrategyByContextKey(context);
        }

        TStrategy? strategyResult = null;

        if (_internalResolver is ContextStrategyResolver<TContext, TStrategy> contextResolver)
            strategyResult = contextResolver.GetStrategy(context);
        else
            strategyResult = Strategies.FirstOrDefault(s => s.CanHandle(context));

        if (strategyResult == null)
            throw new NotSupportedException(
                $"No strategy found for context type '{typeof(TContext).Name}'. Available strategies: {string.Join(", ", Strategies.Select(s => s.GetType().Name))}");

        return strategyResult;
    }

    /// <summary>
    ///     Obtém a estratégia pela chave diretamente (para KeyedStrategy).
    ///     Retorna o tipo específico TStrategy, eliminando a necessidade de casts.
    /// </summary>
    public TStrategy GetStrategy<TKey>(TKey key)
        where TKey : notnull {
        var strategy = Strategies.FirstOrDefault(s => {
            if (s is IKeyedStrategy<TKey, TContext> keyed) return keyed.Key.Equals(key);
            return false;
        });

        if (strategy != null) return strategy;

        throw new NotSupportedException(
            $"No strategy found for key '{key}'. Available strategies: {string.Join(", ", Strategies.Select(s => s.GetType().Name))}");
    }
}

/// <summary>
///     Classe de conveniência para manter compatibilidade com código existente.
///     Usa IStrategy como tipo de estratégia.
/// </summary>
/// <typeparam name="TContext">Tipo do contexto</typeparam>
public class StrategyResolver<TContext> : StrategyResolver<TContext, IStrategy<TContext>>
{
    protected new readonly IEnumerable<IStrategy<TContext>> Strategies;
    private IStrategyResolver<TContext> _internalResolver;

    /// <summary>
    ///     Construtor que auto-detecta O(1) ou O(n) baseado nas estratégias fornecidas.
    /// </summary>
    /// <param name="strategies">Estratégias disponíveis</param>
    /// <param name="keySelector">Seletor de chave para O(1). Se null, usa O(n)</param>
    /// <param name="logger">Logger opcional</param>
    public StrategyResolver(
        IEnumerable<IStrategy<TContext>> strategies,
        Func<TContext, object>? keySelector = null,
        ILogger<StrategyResolver<TContext>>? logger = null)
        : base(strategies, keySelector, logger) {
        Strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
        var strategyList = strategies.ToList();

        if (keySelector != null && strategyList.Count > 0 && strategyList.All(s => IsKeyedStrategy(s)))
            _internalResolver = CreateKeyedResolverWithReflection(strategyList, keySelector, logger);
        else
            _internalResolver = new ContextStrategyResolver<TContext>(
                strategyList,
                logger != null ? new LoggerAdapter<ContextStrategyResolver<TContext>>(logger) : null);
    }

    /// <summary>
    ///     Construtor para classes que herdam e implementam ExecuteAsync customizado.
    /// </summary>
    protected StrategyResolver(IEnumerable<IStrategy<TContext>> strategies)
        : base(strategies) {
        Strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
        _internalResolver = new ContextStrategyResolver<TContext>(strategies);
    }

    /// <summary>
    ///     Construtor genérico para uso com KeyedStrategy quando o tipo da chave é conhecido.
    /// </summary>
    public static StrategyResolver<TContext> CreateKeyed<TKey>(
        IEnumerable<IKeyedStrategy<TKey, TContext>> strategies,
        Func<TContext, TKey> keySelector,
        ILogger<StrategyResolver<TContext>>? logger = null)
        where TKey : notnull {
        var strategyList = strategies.ToList();
        var keyedResolver = new KeyedStrategyResolver<TKey, TContext>(
            strategyList,
            keySelector,
            logger != null ? new LoggerAdapter<KeyedStrategyResolver<TKey, TContext>>(logger) : null);

        return new KeyedStrategyResolverWrapper<TContext>(strategyList, keyedResolver);
    }

    private IStrategyResolver<TContext> CreateKeyedResolverWithReflection(
        List<IStrategy<TContext>> strategies,
        Func<TContext, object> keySelector,
        ILogger<StrategyResolver<TContext>>? logger) {
        try {
            var firstStrategy = strategies[0];
            var keyedInterface = firstStrategy.GetType()
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                                     i.GetGenericTypeDefinition() == typeof(IKeyedStrategy<,>) &&
                                     i.GetGenericArguments()[1] == typeof(TContext));

            if (keyedInterface == null) return new ContextStrategyResolver<TContext>(strategies);

            var keyType = keyedInterface.GetGenericArguments()[0];

            var resolverType = typeof(KeyedStrategyResolver<,>).MakeGenericType(keyType, typeof(TContext));
            var keyedStrategiesList = typeof(List<>).MakeGenericType(keyedInterface);
            var keyedStrategies = Activator.CreateInstance(keyedStrategiesList)!;
            var addMethod = keyedStrategiesList.GetMethod("Add")!;

            foreach (var strategy in strategies) {
                var interfaceImpl = strategy.GetType()
                    .GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType &&
                                         i.GetGenericTypeDefinition() == typeof(IKeyedStrategy<,>) &&
                                         i.GetGenericArguments()[1] == typeof(TContext));
                if (interfaceImpl != null) addMethod.Invoke(keyedStrategies, new[] { strategy });
            }

            var typedKeySelector = CreateTypedKeySelector(keySelector, keyType);
            var constructor = resolverType.GetConstructor(new[] {
                typeof(IEnumerable<>).MakeGenericType(keyedInterface),
                typeof(Func<,>).MakeGenericType(typeof(TContext), keyType),
                typeof(ILogger<>).MakeGenericType(resolverType)
            });

            if (constructor == null) return new ContextStrategyResolver<TContext>(strategies);

            var loggerTyped = logger != null
                ? Activator.CreateInstance(typeof(LoggerAdapter<>).MakeGenericType(resolverType), logger)
                : null;

            return (IStrategyResolver<TContext>)constructor.Invoke(new[]
                { keyedStrategies, typedKeySelector, loggerTyped })!;
        }
        catch {
            return new ContextStrategyResolver<TContext>(strategies);
        }
    }

    private object CreateTypedKeySelector(Func<TContext, object> keySelector, Type keyType) {
        var method = typeof(StrategyResolver<TContext>).GetMethod(
            nameof(CreateKeySelectorHelper),
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null) throw new InvalidOperationException("Failed to create typed key selector");

        var genericMethod = method.MakeGenericMethod(keyType);
        return genericMethod.Invoke(null, new object[] { keySelector })!;
    }

    private static Func<TContext, TKey> CreateKeySelectorHelper<TKey>(Func<TContext, object> keySelector) {
        return ctx => (TKey)keySelector(ctx);
    }

    public new virtual Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default) {
        return _internalResolver.ExecuteAsync(context, cancellationToken);
    }

    private static bool IsKeyedStrategy(IStrategy<TContext> strategy) {
        return strategy.GetType()
            .GetInterfaces()
            .Any(i => i.IsGenericType &&
                      i.GetGenericTypeDefinition() == typeof(IKeyedStrategy<,>) &&
                      i.GetGenericArguments()[1] == typeof(TContext));
    }

    private class KeyedStrategyResolverWrapper<T> : StrategyResolver<T>
    {
        public KeyedStrategyResolverWrapper(IEnumerable<IStrategy<T>> strategies, IStrategyResolver<T> internalResolver)
            : base(strategies) {
            _internalResolver = internalResolver;
        }
    }

    private class LoggerAdapter<T> : ILogger<T>
    {
        private readonly ILogger _logger;

        public LoggerAdapter(ILogger logger) {
            _logger = logger;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
            return _logger.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel) {
            return _logger.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) {
            _logger.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}