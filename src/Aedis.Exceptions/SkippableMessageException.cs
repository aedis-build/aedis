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
    protected SkippableMessageException(string message, string reason) : base(message) {
        Reason = reason;
    }

    protected SkippableMessageException(string message, string reason, Exception innerException)
        : base(message, innerException) {
        Reason = reason;
    }

    public string Reason { get; }
}