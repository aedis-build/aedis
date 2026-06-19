namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando uma mensagem está sendo processada por outra instância (distributed lock).
///     Comportamento no RabbitMQ: NACK requeue=true (mensagem volta imediatamente para a fila).
///     Não incrementa death count pois não é um erro, apenas concorrência esperada em multi-instância.
/// </summary>
public class MessageBeingProcessedException : RetryableException
{
    /// <summary>Cria a exceção identificando a mensagem, a chave do lock distribuído e a instância que está processando.</summary>
    public MessageBeingProcessedException(
        Guid messageId,
        string lockKey,
        string processingInstance = "unknown")
        : base($"Mensagem {messageId} está sendo processada por outra instância ({processingInstance})") {
        LockKey = lockKey;
        ProcessingInstance = processingInstance;
    }

    /// <summary>Chave do lock distribuído que protege o processamento da mensagem.</summary>
    public string LockKey { get; }

    /// <summary>Identificação da instância que detém o lock e está processando a mensagem.</summary>
    public string ProcessingInstance { get; }

    /// <summary>Sempre verdadeiro: a mensagem volta para a fila por se tratar de concorrência esperada, não de erro.</summary>
    public override bool ShouldRequeue => true;

    /// <summary>Atraso curto (1 s) antes da próxima tentativa, dando tempo para o lock ser liberado.</summary>
    public override TimeSpan? RetryAfter => TimeSpan.FromSeconds(1);
}