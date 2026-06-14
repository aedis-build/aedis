using Aedis.Domain.ChainOfResponsibility.Abstractions;
namespace Aedis.Domain.ChainOfResponsibility;

/// <summary>
///     A handler that executes multiple handlers in sequence and combines their results.
///     Useful when you need to aggregate responses from multiple handlers.
/// </summary>
/// <typeparam name="TRequest">The type of request to be handled</typeparam>
/// <typeparam name="TResponse">The type of response returned after handling</typeparam>
public abstract class CompositeHandler<TRequest, TResponse> : HandlerBase<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly List<IHandler<TRequest, TResponse>> _handlers = new();

    /// <summary>
    ///     Adds a handler to the composite collection.
    /// </summary>
    /// <param name="handler">The handler to add</param>
    protected void AddHandler(IHandler<TRequest, TResponse> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(handler);
    }

    /// <summary>
    ///     Determines whether this composite handler can process the request.
    ///     By default, returns true if any child handler can process the request.
    /// </summary>
    protected override async Task<bool> CanHandleAsync(TRequest request, CancellationToken cancellationToken) {
        foreach (var handler in _handlers) {
            var response = await handler.HandleAsync(request, cancellationToken);
            if (response != null) return true;
        }

        return false;
    }

    /// <summary>
    ///     Processes the request by executing all child handlers and combining their results.
    ///     Override CombineResponses to customize how responses are merged.
    /// </summary>
    protected override async Task<TResponse> ProcessAsync(TRequest request, CancellationToken cancellationToken) {
        var responses = new List<TResponse>();

        foreach (var handler in _handlers) {
            var response = await handler.HandleAsync(request, cancellationToken);
            if (response != null) responses.Add(response);
        }

        return CombineResponses(responses);
    }

    /// <summary>
    ///     Combines multiple responses into a single response.
    ///     Override this method to implement custom response aggregation logic.
    /// </summary>
    /// <param name="responses">Collection of responses from child handlers</param>
    /// <returns>The combined response</returns>
    protected abstract TResponse CombineResponses(IReadOnlyList<TResponse> responses);
}