namespace Aedis.Domain.Saga.Abstractions;

/// <summary>
///     Persistência opcional do estado de uma saga, permitindo retomada, auditoria e diagnóstico após
///     reinícios. Implemente para gravar o progresso em um meio durável (banco, cache); quando nenhum store
///     é fornecido, a saga opera apenas em memória.
/// </summary>
public interface ISagaStateStore
{
    /// <summary>Registra o início de uma saga, criando o estado inicial com status "Running".</summary>
    Task SaveSagaStartAsync(Guid sagaId, string sagaType, CancellationToken ct = default);

    /// <summary>Registra a conclusão bem-sucedida de uma step, gravando seus dados de saída.</summary>
    Task SaveStepCompletionAsync(Guid sagaId, string stepName, object? data, CancellationToken ct = default);

    /// <summary>Recupera o estado persistido de uma saga, ou null se não houver registro.</summary>
    Task<SagaState?> GetSagaStateAsync(Guid sagaId, CancellationToken ct = default);

    /// <summary>Marca a saga como concluída com sucesso.</summary>
    Task MarkAsCompletedAsync(Guid sagaId, CancellationToken ct = default);

    /// <summary>Marca a saga como falha, registrando a mensagem de erro.</summary>
    Task MarkAsFailedAsync(Guid sagaId, string errorMessage, CancellationToken ct = default);

    /// <summary>Marca a saga como compensada, registrando quantas steps foram revertidas.</summary>
    Task MarkAsCompensatedAsync(Guid sagaId, int stepsCompensated, CancellationToken ct = default);
}

/// <summary>
///     Estado persistido de uma saga: identidade, tipo, status atual, steps executadas e marcos de tempo.
/// </summary>
public class SagaState
{
    /// <summary>Identificador único da saga.</summary>
    public Guid SagaId { get; set; }

    /// <summary>Nome lógico/tipo da saga.</summary>
    public string SagaType { get; set; } = string.Empty;

    /// <summary>Status atual: "Running", "Completed", "Failed" ou "Compensated".</summary>
    public string Status { get; set; } = "Running";

    /// <summary>Estados individuais das steps já registradas.</summary>
    public List<SagaStepState> Steps { get; set; } = new();

    /// <summary>Instante de início da saga.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Instante de conclusão (null enquanto em andamento).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Mensagem de erro em caso de falha.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
///     Estado persistido de uma step individual dentro de uma <see cref="SagaState" />.
/// </summary>
public class SagaStepState
{
    /// <summary>Nome único da step.</summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>Status da step: "Pending", "Completed" ou "Compensated".</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Instante em que a step foi executada.</summary>
    public DateTimeOffset ExecutedAt { get; set; }

    /// <summary>Dados de saída da step, serializados.</summary>
    public string? Data { get; set; }

    /// <summary>Mensagem de erro associada à step, se houver.</summary>
    public string? ErrorMessage { get; set; }
}
