namespace Aedis.Exceptions;

/// <summary>
///     Exceção base para mensagens que devem ser descartadas (ACK) imediatamente sem processamento.
///     Comportamento no RabbitMQ: Sempre faz ACK (mensagem sai da fila).
///     Diferença de PermanentFailureException:
///     - SkippableMessageException: ACK direto (ex: mensagem duplicada, já processada)
///     - PermanentFailureException: Vai para DLQ se habilitada (ex: erro de validação permanente)
///     Exemplos de uso: mensagem duplicada (idempotência), mensagem expirada, formato inválido.
/// </summary>
public abstract class SkippableMessageException : Exception
{
    /// <summary>Cria a exceção com a mensagem e o <paramref name="reason" /> que classifica o descarte (ex.: "duplicate", "expired").</summary>
    protected SkippableMessageException(string message, string reason) : base(message) {
        Reason = reason;
    }

    /// <summary>Cria a exceção encadeando a causa original (<paramref name="innerException" />).</summary>
    protected SkippableMessageException(string message, string reason, Exception innerException)
        : base(message, innerException) {
        Reason = reason;
    }

    /// <summary>Motivo do descarte da mensagem, usado para classificação e logging (ex.: "duplicate", "expired").</summary>
    public string Reason { get; }
}