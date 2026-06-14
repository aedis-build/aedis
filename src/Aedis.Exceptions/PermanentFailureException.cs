namespace Aedis.Exceptions;

/// <summary>
///     Exceção base para erros permanentes que não devem ser retentados.
///     Comportamento no RabbitMQ:
///     - Se EnableFinalDLQ = true: Envia DIRETO para DLQ (NACK requeue=false)
///     - Se EnableFinalDLQ = false: Descarta com ACK
///     NÃO passa por health retry nem retry com backoff.
///     Exemplos de uso: validação de negócio falhou, esquema inválido, dados corrompidos.
/// </summary>
public abstract class PermanentFailureException : Exception
{
    protected PermanentFailureException(string message) : base(message) { }

    protected PermanentFailureException(string message, Exception innerException) : base(message, innerException) { }

    public virtual bool ShouldRequeue => false;
    public virtual bool SendToDeadLetterQueue => true;
    public Dictionary<string, object> ErrorData { get; } = new();

    public void AddErrorData(string key, object value) {
        ErrorData[key] = value;
    }
}