namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando uma mensagem duplicada é detectada (já foi processada).
///     Herda de SkippableMessageException para garantir que a mensagem seja ACK'd e descartada sem retry.
/// </summary>
public class DuplicateMessageException : SkippableMessageException
{
    /// <summary>Cria a exceção identificando a mensagem duplicada e, opcionalmente, a chave de idempotência que a detectou.</summary>
    public DuplicateMessageException(Guid messageId, string message, string? idempotencyKey = null)
        : base(message, "duplicate") {
        MessageId = messageId;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Identificador da mensagem detectada como duplicada.</summary>
    public Guid MessageId { get; }

    /// <summary>Chave de idempotência que identificou a duplicidade, quando disponível.</summary>
    public string? IdempotencyKey { get; }
}