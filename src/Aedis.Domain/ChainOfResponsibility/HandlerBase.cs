namespace Aedis.Domain.ChainOfResponsibility;

/// <summary>
///     Abstract base class for implementing handlers in the chain of responsibility pattern.
///     Provides the common functionality for chaining and delegation.
/// </summary>
/// <typeparam name="TRequest">The type of request to be handled</typeparam>
/// <typeparam name="TResponse">The type of response returned after handling</typeparam>
public abstract class HandlerBase<TRequest, TResponse> : IHandler<TRequest, TResponse>
    where TRequest : notnull
{
    private IHandler<TRequest, TResponse>? _nextHandler;

    /// <summary>
    ///     Sets the next handler in the chain.
    /// </summary>
    /// <param name="handler">The next handler to be called if this handler cannot process the request</param>
    /// <returns>The next handler for fluent configuration</returns>
    public virtual IHandler<TRequest, TResponse> SetNext(IHandler<TRequest, TResponse> handler) {
        _nextHandler = handler;
        return handler;
    }

    /// <summary>
    ///     Handles the request by first checking if this handler can process it.
    ///     If CanHandleAsync returns true, processes the request via ProcessAsync.
    ///     ALWAYS delegates to the next handler (if exists), enabling both Chain of Responsibility and Pipeline patterns.
    /// </summary>
    /// <param name="request">The request to be handled</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>The response after handling the request, or null if no handler could process it</returns>
    public virtual async Task<TResponse?> HandleAsync(TRequest request, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        var currentRequest = request;

        if (await CanHandleAsync(request, cancellationToken)) {
            var processedResult = await ProcessAsync(request, cancellationToken);

            if (processedResult is TRequest modifiedRequest) currentRequest = modifiedRequest;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_nextHandler != null) return await _nextHandler.HandleAsync(currentRequest, cancellationToken);

        if (currentRequest is TResponse response) return response;

        return default;
    }

    /// <summary>
    ///     Determines whether this handler can process the given request.
    ///     Override this method to implement custom logic for determining if the handler is responsible.
    /// </summary>
    /// <param name="request">The request to evaluate</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if this handler can process the request; otherwise, false</returns>
    protected abstract Task<bool> CanHandleAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     Processes the request. This method is only called if CanHandleAsync returns true.
    ///     Override this method to implement the actual request processing logic.
    /// </summary>
    /// <param name="request">The request to process</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>The response after processing the request</returns>
    protected abstract Task<TResponse> ProcessAsync(TRequest request, CancellationToken cancellationToken);
}