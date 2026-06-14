namespace Aedis.Domain.Saga.Abstractions;

/// <summary>
///     Representa uma etapa (step) de uma Saga.
///     Cada step deve ser idempotente e implementar lógica de compensação.
/// </summary>
/// <typeparam name="TContext">Tipo do contexto da saga</typeparam>
public interface ISagaStep<TContext> where TContext : ISagaContext
{
    /// <summary>
    ///     Nome único da step para identificação e logging
    /// </summary>
    string StepName { get; }

    /// <summary>
    ///     Executa a lógica de negócio da step
    /// </summary>
    /// <param name="context">Contexto compartilhado da saga</param>
    /// <param name="ct">Token de cancelamento</param>
    /// <returns>Resultado da execução contendo sucesso/falha e dados</returns>
    Task<SagaStepResult> ExecuteAsync(TContext context, CancellationToken ct = default);

    /// <summary>
    ///     Compensa a step em caso de falha posterior na saga.
    ///     Deve reverter as mudanças feitas no ExecuteAsync.
    /// </summary>
    /// <param name="context">Contexto compartilhado da saga</param>
    /// <param name="executionResult">Resultado da execução original</param>
    /// <param name="ct">Token de cancelamento</param>
    Task CompensateAsync(TContext context, SagaStepResult executionResult, CancellationToken ct = default);
}