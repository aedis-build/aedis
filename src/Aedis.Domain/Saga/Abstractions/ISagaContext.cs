namespace Aedis.Domain.Saga.Abstractions;

/// <summary>
///     Contexto compartilhado entre todas as steps de uma saga.
///     Interface genérica que permite diferentes tipos de sagas (Database, Mensageria, HTTP, Híbridas).
/// </summary>
public interface ISagaContext
{
    /// <summary>
    ///     Identificador único da instância da saga
    /// </summary>
    Guid SagaId { get; }

    /// <summary>
    ///     Nome/tipo da saga (para identificação)
    /// </summary>
    string SagaType { get; }

    /// <summary>
    ///     Dados compartilhados entre steps (ex: IDs gerados, entidades)
    /// </summary>
    IDictionary<string, object> SharedData { get; }

    /// <summary>
    ///     Metadados para observability (tracing, logging)
    /// </summary>
    IDictionary<string, string> Metadata { get; }

    /// <summary>
    ///     Data/hora de início da saga
    /// </summary>
    DateTimeOffset StartedAt { get; }
}