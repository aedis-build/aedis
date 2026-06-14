namespace Aedis.Domain.Saga;

/// <summary>
///     Resultado da execução de uma step individual
/// </summary>
public class SagaStepResult
{
    /// <summary>
    ///     Indica se a step foi executada com sucesso
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    ///     Dados retornados pela step (ex: entidade criada, ID gerado)
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    ///     Mensagem de erro em caso de falha
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    ///     Exceção original em caso de falha
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    ///     Cria resultado de sucesso
    /// </summary>
    public static SagaStepResult Successful(object? data = null) {
        return new SagaStepResult { Success = true, Data = data };
    }

    /// <summary>
    ///     Cria resultado de falha
    /// </summary>
    public static SagaStepResult Failed(string errorMessage, Exception? exception = null) {
        return new SagaStepResult {
            Success = false,
            ErrorMessage = errorMessage,
            Exception = exception
        };
    }
}