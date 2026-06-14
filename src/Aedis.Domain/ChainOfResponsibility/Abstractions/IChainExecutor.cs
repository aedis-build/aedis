namespace Aedis.Domain.ChainOfResponsibility.Abstractions;

/// <summary>
///     Interface para executar uma cadeia de handlers.
///     Facilita testes unitários permitindo mocking.
/// </summary>
/// <typeparam name="TRequest">O tipo da requisição</typeparam>
/// <typeparam name="TResponse">O tipo da resposta</typeparam>
public interface IChainExecutor<TRequest, TResponse> where TRequest : notnull
{
    /// <summary>
    ///     Executa a cadeia de handlers processando a requisição.
    ///     Retorna null se nenhum handler processar a requisição.
    /// </summary>
    /// <param name="request">A requisição a ser processada.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A resposta processada pela cadeia ou null.</returns>
    Task<TResponse?> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executa a cadeia de handlers com uma resposta padrão caso nenhum handler processe a requisição.
    /// </summary>
    /// <param name="request">A requisição a ser processada.</param>
    /// <param name="defaultResponse">A resposta padrão se nenhum handler processar.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A resposta processada pela cadeia ou a resposta padrão.</returns>
    Task<TResponse> ExecuteWithDefaultAsync(TRequest request, TResponse defaultResponse,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executa a cadeia de handlers com uma factory para criar a resposta padrão caso nenhum handler processe a
    ///     requisição.
    /// </summary>
    /// <param name="request">A requisição a ser processada.</param>
    /// <param name="defaultResponseFactory">Factory para criar a resposta padrão.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A resposta processada pela cadeia ou a resposta da factory.</returns>
    Task<TResponse> ExecuteWithDefaultAsync(TRequest request, Func<TResponse> defaultResponseFactory,
        CancellationToken cancellationToken = default);
}