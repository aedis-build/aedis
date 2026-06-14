using Aedis.Domain.Saga.Abstractions;

namespace Aedis.Domain.Saga;

/// <summary>
///     Implementação base de contexto de saga genérico.
///     Não contém dependências específicas de infraestrutura (Database, Mensageria, etc).
///     <para>
///         Para usar saga com banco de dados, o usuário deve criar seu próprio contexto
///         herdando de <see cref="SagaContext" /> e incluir <see cref="IUnitOfWork" /> como propriedade opcional.
///         A gestão de transações deve ser feita externamente (no handler que chama a saga).
///     </para>
/// </summary>
public abstract class SagaContext : ISagaContext
{
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

    public Guid SagaId { get; }

    public string SagaType { get; }

    public IDictionary<string, object> SharedData { get; }

    public IDictionary<string, string> Metadata { get; }

    public DateTimeOffset StartedAt { get; }
}