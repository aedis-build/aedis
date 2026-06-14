namespace Aedis.Domain.ChainOfResponsibility.Abstractions;

/// <summary>
///     Provides a fluent interface for building chains of handlers.
/// </summary>
/// <typeparam name="TRequest">The type of request to be handled</typeparam>
/// <typeparam name="TResponse">The type of response returned after handling</typeparam>
public interface IChainBuilder<TRequest, TResponse> where TRequest : notnull
{
    /// <summary>
    ///     Adds a handler to the chain.
    /// </summary>
    /// <param name="handler">The handler to add to the chain</param>
    /// <returns>The builder for fluent configuration</returns>
    IChainBuilder<TRequest, TResponse> AddHandler(IHandler<TRequest, TResponse> handler);

    /// <summary>
    ///     Adds a handler to the chain using a factory function.
    /// </summary>
    /// <param name="handlerFactory">Factory function to create the handler</param>
    /// <returns>The builder for fluent configuration</returns>
    IChainBuilder<TRequest, TResponse> AddHandler(Func<IHandler<TRequest, TResponse>> handlerFactory);

    /// <summary>
    ///     Builds and returns the first handler in the chain.
    /// </summary>
    /// <returns>The first handler in the chain, or null if no handlers were added</returns>
    IHandler<TRequest, TResponse>? Build();
}