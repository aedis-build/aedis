namespace Aedis.Domain.Saga;

/// <summary>
///     Resultado da execução completa de uma saga
/// </summary>
public class SagaExecutionResult
{
    /// <summary>
    ///     Indica se a saga foi executada com sucesso
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    ///     Número de steps executadas
    /// </summary>
    public int StepsExecuted { get; init; }

    /// <summary>
    ///     Número de steps compensadas (em caso de falha)
    /// </summary>
    public int StepsCompensated { get; init; }

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
    public static SagaExecutionResult Successful(int stepsExecuted) {
        return new SagaExecutionResult {
            Success = true,
            StepsExecuted = stepsExecuted,
            StepsCompensated = 0
        };
    }

    /// <summary>
    ///     Cria resultado de falha com compensação
    /// </summary>
    public static SagaExecutionResult Failed(
        string errorMessage,
        int stepsExecuted,
        int stepsCompensated,
        Exception? exception = null) {
        return new SagaExecutionResult {
            Success = false,
            StepsExecuted = stepsExecuted,
            StepsCompensated = stepsCompensated,
            ErrorMessage = errorMessage,
            Exception = exception
        };
    }
}