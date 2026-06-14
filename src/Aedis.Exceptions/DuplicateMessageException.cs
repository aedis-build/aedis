namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando uma mensagem duplicada é detectada (já foi processada).
///     Herda de SkippableMessageException para garantir que a mensagem seja ACK'd e descartada sem retry.
/// </summary>
public class DuplicateMessageException : SkippableMessageException
{
    public DuplicateMessageException(Guid messageId, string message, string? idempotencyKey = null)
        : base(message, "duplicate") {
        MessageId = messageId;
        IdempotencyKey = idempotencyKey;
    }

    public Guid MessageId { get; }
    public string? IdempotencyKey { get; }
}