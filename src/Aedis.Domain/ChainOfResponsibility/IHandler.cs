namespace Aedis.Domain.ChainOfResponsibility;

/// <summary>
///     Defines the contract for a handler in the chain of responsibility pattern.
///     Each handler can process a request or pass it to the next handler in the chain.
/// </summary>
/// <typeparam name="TRequest">The type of request to be handled</typeparam>
/// <typeparam name="TResponse">The type of response returned after handling</typeparam>
public interface IHandler<TRequest, TResponse> where TRequest : notnull
{
    /// <summary>
    ///     Sets the next handler in the chain.
    /// </summary>
    /// <param name="handler">The next handler to be called if this handler cannot process the request</param>
    /// <returns>The next handler for fluent configuration</returns>
    IHandler<TRequest, TResponse> SetNext(IHandler<TRequest, TResponse> handler);

    /// <summary>
    ///     Handles the request. If the handler can process it, returns a response.
    ///     Otherwise, passes the request to the next handler in the chain.
    /// </summary>
    /// <param name="request">The request to be handled</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>The response after handling the request, or null if no handler could process it</returns>
    Task<TResponse?> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}