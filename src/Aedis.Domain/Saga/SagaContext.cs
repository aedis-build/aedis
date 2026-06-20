using Aedis.Domain.Saga.Abstractions;

namespace Aedis.Domain.Saga;

/// <summary>
///     Implementação base de contexto de saga genérico.
///     Não contém dependências específicas de infraestrutura (Database, Mensageria, etc).
///     <para>
///         Para usar saga com banco de dados, o usuário deve criar seu próprio contexto
///         herdando de <see cref="SagaContext" /> e incluir <c>IUnitOfWork</c> como propriedade opcional.
///         A gestão de transações deve ser feita externamente (no handler que chama a saga).
///     </para>
/// </summary>
public abstract class SagaContext : ISagaContext
{
    /// <summary>
    ///     Inicializa o contexto com o nome lógico da saga e, opcionalmente, um id (gerado quando omitido).
    ///     Carimba <see cref="StartedAt" /> e popula <see cref="Metadata" /> com dados de observability
    ///     (máquina, processo, instante de criação).
    /// </summary>
    protected SagaContext(
        string sagaType,
        Guid? sagaId = null) {
        SagaType = sagaType ?? throw new ArgumentNullException(nameof(sagaType));
        SagaId = sagaId ?? Guid.NewGuid();
        StartedAt = DateTimeOffset.UtcNow;

        SharedData = new Dictionary<string, object>();
        Metadata = new Dictionary<string, string> {
            ["CreatedAt"] = StartedAt.ToString("O"),
            ["MachineName"] = Environment.MachineName,
            ["ProcessId"] = Environment.ProcessId.ToString()
        };
    }

    /// <inheritdoc />
    public Guid SagaId { get; }

    /// <inheritdoc />
    public string SagaType { get; }

    /// <inheritdoc />
    public IDictionary<string, object> SharedData { get; }

    /// <inheritdoc />
    public IDictionary<string, string> Metadata { get; }

    /// <inheritdoc />
    public DateTimeOffset StartedAt { get; }
}