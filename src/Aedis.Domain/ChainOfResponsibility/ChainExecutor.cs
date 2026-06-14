using Microsoft.Extensions.Logging;

namespace Aedis.Domain.ChainOfResponsibility;

/// <summary>
///     Provides execution capabilities for handler chains with logging and error handling.
///     Wraps the chain execution with cross-cutting concerns like logging and exception handling.
/// </summary>
/// <typeparam name="TRequest">The type of request to be handled</typeparam>
/// <typeparam name="TResponse">The type of response returned after handling</typeparam>
public sealed class ChainExecutor<TRequest, TResponse> : IChainExecutor<TRequest, TResponse> where TRequest : notnull
{
    private readonly IHandler<TRequest, TResponse> _firstHandler;
    private readonly ILogger<ChainExecutor<TRequest, TResponse>>? _logger;

    /// <summary>
    ///     Initializes a new instance of ChainExecutor with the specified chain.
    /// </summary>
    /// <param name="firstHandler">The first handler in the chain</param>
    /// <param name="logger">Optional logger for tracking execution</param>
    public ChainExecutor(
        IHandler<TRequest, TResponse> firstHandler,
        ILogger<ChainExecutor<TRequest, TResponse>>? logger = null) {
        _firstHandler = firstHandler ?? throw new ArgumentNullException(nameof(firstHandler));
        _logger = logger;
    }

    /// <summary>
    ///     Executes the chain with the given request.
    ///     Logs execution details and handles exceptions gracefully.
    /// </summary>
    /// <param name="request">The request to process</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>The response from the chain, or null if no handler processed the request</returns>
    public async Task<TResponse?> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        _logger?.LogDebug("Starting chain execution for request type {RequestType}", typeof(TRequest).Name);

        try {
            var response = await _firstHandler.HandleAsync(request, cancellationToken);

            if (response == null)
                _logger?.LogWarning("No handler processed the request of type {RequestType}", typeof(TRequest).Name);
            else
                _logger?.LogDebug("Chain execution completed successfully for request type {RequestType}",
                    typeof(TRequest).Name);

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            _logger?.LogDebug("Chain execution was cancelled for request type {RequestType}",
                typeof(TRequest).Name);
            throw;
        }
        catch (Exception ex) {
            _logger?.LogError(ex, "Error during chain execution for request type {RequestType}", typeof(TRequest).Name);
            throw;
        }
    }

    /// <summary>
    ///     Executes the chain with a default response if no handler processes the request.
    /// </summary>
    /// <param name="request">The request to process</param>
    /// <param name="defaultResponse">The default response to return if no handler processes the request</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>The response from the chain, or the default response</returns>
    public async Task<TResponse> ExecuteWithDefaultAsync(
        TRequest request,
        TResponse defaultResponse,
        CancellationToken cancellationToken = default) {
        var response = await ExecuteAsync(request, cancellationToken);
        return response ?? defaultResponse;
    }

    /// <summary>
    ///     Executes the chain with a factory function for the default response.
    ///     The factory is only called if no handler processes the request.
    /// </summary>
    /// <param name="request">The request to process</param>
    /// <param name="defaultResponseFactory">Factory function to create the default response</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>The response from the chain, or the default response from the factory</returns>
    public async Task<TResponse> ExecuteWithDefaultAsync(
        TRequest request,
        Func<TResponse> defaultResponseFactory,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(defaultResponseFactory);

        var response = await ExecuteAsync(request, cancellationToken);
        return response ?? defaultResponseFactory();
    }
}