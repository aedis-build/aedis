namespace Aedis.Domain.Saga;

/// <summary>
///     Exceção lançada quando uma saga falha durante a execução
/// </summary>
public class SagaExecutionException : Exception
{
    public SagaExecutionException(
        Guid sagaId,
        string message,
        string? failedStepName = null,
        Exception? innerException = null)
        : base(message, innerException) {
        SagaId = sagaId;
        FailedStepName = failedStepName;
    }

    /// <summary>
    ///     Nome da step que causou a falha
    /// </summary>
    public string? FailedStepName { get; }

    /// <summary>
    ///     ID da saga que falhou
    /// </summary>
    public Guid SagaId { get; }
}