namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando uma mensagem está sendo processada por outra instância (distributed lock).
///     Comportamento no RabbitMQ: NACK requeue=true (mensagem volta imediatamente para a fila).
///     Não incrementa death count pois não é um erro, apenas concorrência esperada em multi-instância.
/// </summary>
public class MessageBeingProcessedException : RetryableException
{
    public MessageBeingProcessedException(
        Guid messageId,
        string lockKey,
        string processingInstance = "unknown")
        : base($"Mensagem {messageId} está sendo processada por outra instância ({processingInstance})") {
        LockKey = lockKey;
        ProcessingInstance = processingInstance;
    }

    public string LockKey { get; }
    public string ProcessingInstance { get; }

    public override bool ShouldRequeue => true;

    public override TimeSpan? RetryAfter => TimeSpan.FromSeconds(1);
}