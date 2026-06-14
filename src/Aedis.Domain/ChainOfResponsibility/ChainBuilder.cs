using Aedis.Domain.ChainOfResponsibility.Abstractions;
namespace Aedis.Domain.ChainOfResponsibility;

/// <summary>
///     Default implementation of IChainBuilder for constructing chains of handlers.
///     Provides a fluent API for building and configuring handler chains.
/// </summary>
/// <typeparam name="TRequest">The type of request to be handled</typeparam>
/// <typeparam name="TResponse">The type of response returned after handling</typeparam>
public sealed class ChainBuilder<TRequest, TResponse> : IChainBuilder<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly List<IHandler<TRequest, TResponse>> _handlers = new();

    /// <summary>
    ///     Adds a handler to the chain.
    /// </summary>
    /// <param name="handler">The handler to add to the chain</param>
    /// <returns>The builder for fluent configuration</returns>
    public IChainBuilder<TRequest, TResponse> AddHandler(IHandler<TRequest, TResponse> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(handler);
        return this;
    }

    /// <summary>
    ///     Adds a handler to the chain using a factory function.
    ///     Useful for dependency injection scenarios.
    /// </summary>
    /// <param name="handlerFactory">Factory function to create the handler</param>
    /// <returns>The builder for fluent configuration</returns>
    public IChainBuilder<TRequest, TResponse> AddHandler(Func<IHandler<TRequest, TResponse>> handlerFactory) {
        ArgumentNullException.ThrowIfNull(handlerFactory);
        var handler = handlerFactory();
        return AddHandler(handler);
    }

    /// <summary>
    ///     Builds and returns the first handler in the chain.
    ///     Links all handlers in the order they were added.
    /// </summary>
    /// <returns>The first handler in the chain, or null if no handlers were added</returns>
    public IHandler<TRequest, TResponse>? Build() {
        if (_handlers.Count == 0) return null;

        for (var i = 0; i < _handlers.Count - 1; i++) _handlers[i].SetNext(_handlers[i + 1]);

        return _handlers[0];
    }

    /// <summary>
    ///     Creates a new instance of ChainBuilder.
    /// </summary>
    /// <returns>A new chain builder instance</returns>
    public static IChainBuilder<TRequest, TResponse> Create() {
        return new ChainBuilder<TRequest, TResponse>();
    }
}