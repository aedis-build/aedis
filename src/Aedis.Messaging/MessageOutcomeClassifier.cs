using Aedis.Exceptions;
using Aedis.Messaging.Abstractions;

namespace Aedis.Messaging;

/// <summary>
///     Classifica o desfecho do processamento de uma mensagem a partir da exceção lançada pelo handler,
///     segundo a taxonomia de <c>Aedis.Exceptions</c>. É a regra única que os providers de broker usam para
///     decidir ACK/NACK/dead-letter e que a telemetria usa na tag <c>outcome</c> — assim todos os transportes
///     reagem da mesma forma ao mesmo erro.
/// </summary>
public static class MessageOutcomeClassifier
{
    /// <summary>Deriva o <see cref="MessageOutcome" /> da exceção; <c>null</c> significa sucesso.</summary>
    public static MessageOutcome FromException(Exception? exception) {
        return exception switch {
            null => MessageOutcome.Success,
            DuplicateMessageException => MessageOutcome.Duplicate,
            SkippableMessageException => MessageOutcome.Skipped,
            PermanentFailureException => MessageOutcome.PermanentFailure,
            RetryableException => MessageOutcome.Retryable,
            ExternalServiceException { ShouldRequeue: true } => MessageOutcome.ExternalRetryable,
            ExternalServiceException => MessageOutcome.ExternalPermanent,
            OperationCanceledException => MessageOutcome.Cancelled,
            _ => MessageOutcome.UnhandledFailure
        };
    }

    /// <summary>Verdadeiro quando o desfecho pede nova tentativa (a mensagem deve voltar à fila).</summary>
    public static bool ShouldRetry(MessageOutcome outcome) =>
        outcome is MessageOutcome.Retryable or MessageOutcome.ExternalRetryable or MessageOutcome.Cancelled
            or MessageOutcome.UnhandledFailure;

    /// <summary>Verdadeiro quando o desfecho encaminha a mensagem à dead-letter queue (quando habilitada).</summary>
    public static bool ShouldDeadLetter(MessageOutcome outcome) =>
        outcome is MessageOutcome.PermanentFailure or MessageOutcome.ExternalPermanent;

    /// <summary>Verdadeiro quando a mensagem deve ser confirmada (ACK) sem nova tentativa nem dead-letter.</summary>
    public static bool ShouldAcknowledge(MessageOutcome outcome) =>
        outcome is MessageOutcome.Success or MessageOutcome.Duplicate or MessageOutcome.Skipped;

    /// <summary>Valor estável da tag <c>outcome</c> para métricas e spans (<c>snake_case</c>).</summary>
    public static string ToTagValue(MessageOutcome outcome) {
        return outcome switch {
            MessageOutcome.Success => "success",
            MessageOutcome.Duplicate => "duplicate",
            MessageOutcome.Skipped => "skipped",
            MessageOutcome.PermanentFailure => "permanent_failure",
            MessageOutcome.Retryable => "retryable",
            MessageOutcome.ExternalRetryable => "external_retryable",
            MessageOutcome.ExternalPermanent => "external_permanent",
            MessageOutcome.Cancelled => "cancelled",
            _ => "unhandled_failure"
        };
    }
}
