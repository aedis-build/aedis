namespace Aedis.Domain.Saga.Abstractions;

/// <summary>
///     Orquestrador de Saga que gerencia execução de steps e compensação automática.
///     Implementa IAsyncDisposable para auto-compensação em caso de falha.
/// </summary>
/// <typeparam name="TContext">Tipo do contexto da saga</typeparam>
public interface ISaga<TContext> : IAsyncDisposable where TContext : ISagaContext
{
    /// <summary>
    ///     Identificador único da saga
    /// </summary>
    Guid SagaId { get; }

    /// <summary>
    ///     Adiciona uma step à saga
    /// </summary>
    ISaga<TContext> AddStep(ISagaStep<TContext> step);

    /// <summary>
    ///     Executa todas as steps sequencialmente dentro de uma transação.
    ///     Em caso de falha, a compensação será executada automaticamente no Dispose.
    /// </summary>
    Task<SagaExecutionResult> ExecuteAsync(TContext context, CancellationToken ct = default);

    /// <summary>
    ///     Marca a saga como completada com sucesso.
    ///     Deve ser chamado após ExecuteAsync() para evitar compensação no Dispose.
    /// </summary>
    Task CompleteAsync(CancellationToken ct = default);
}