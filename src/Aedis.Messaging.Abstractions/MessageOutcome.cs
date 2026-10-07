namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Desfecho normalizado do processamento de uma mensagem, derivado da taxonomia de exceções do Aedis. É a
///     classificação única que todos os providers de broker e a telemetria de mensageria honram: decide
///     ACK/NACK/dead-letter e vira a tag <c>outcome</c> das métricas e spans.
/// </summary>
public enum MessageOutcome
{
    /// <summary>Handler concluiu sem exceção: ACK.</summary>
    Success,

    /// <summary>Mensagem já processada (idempotência): ACK sem retry.</summary>
    Duplicate,

    /// <summary>Mensagem descartada deliberadamente (expirada, formato inválido): ACK sem retry.</summary>
    Skipped,

    /// <summary>Falha permanente: dead-letter quando habilitada, senão descarte.</summary>
    PermanentFailure,

    /// <summary>Falha recuperável do próprio processamento: nova tentativa.</summary>
    Retryable,

    /// <summary>Serviço externo falhou de forma transitória (sem status, 401/408/429/5xx): nova tentativa.</summary>
    ExternalRetryable,

    /// <summary>Serviço externo recusou de forma definitiva (4xx não transitório): dead-letter.</summary>
    ExternalPermanent,

    /// <summary>Processamento cancelado (shutdown): a mensagem volta à fila sem contar como falha.</summary>
    Cancelled,

    /// <summary>Exceção fora da taxonomia: tratada como falha com retry pela política do consumer.</summary>
    UnhandledFailure
}
