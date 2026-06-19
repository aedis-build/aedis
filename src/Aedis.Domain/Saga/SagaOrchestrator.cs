using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Aedis.Domain.Saga.Abstractions;

namespace Aedis.Domain.Saga;

/// <summary>
///     Orquestrador de saga que executa steps sequencialmente e, ao primeiro erro, compensa em ordem
///     reversa (LIFO) as steps já concluídas. Use adicionando steps via <see cref="AddStep" /> e chamando
///     <see cref="ExecuteAsync" />; em sucesso, chame <see cref="CompleteAsync" /> para evitar a
///     auto-compensação disparada no descarte. Opcionalmente persiste o progresso num
///     <see cref="ISagaStateStore" />.
/// </summary>
/// <remarks>
///     A contagem de steps executadas é sempre capturada antes de compensar, pois a compensação esvazia a
///     stack interna. O orquestrador é de uso único: uma instância não pode ser reexecutada.
/// </remarks>
/// <typeparam name="TContext">Tipo do contexto da saga</typeparam>
public class SagaOrchestrator<TContext> : ISaga<TContext> where TContext : ISagaContext
{
    private readonly Stack<(ISagaStep<TContext> Step, SagaStepResult Result)> _executedSteps = new();
    private readonly ILogger<SagaOrchestrator<TContext>> _logger;
    private readonly ISagaStateStore? _stateStore;
    private readonly List<ISagaStep<TContext>> _steps = new();
    private bool _disposed;
    private SagaExecutionResult? _executionResult;

    private bool _isCompleted;
    private bool _isExecuted;

    /// <summary>
    ///     Cria a saga com seu <paramref name="logger" />, um <paramref name="sagaId" /> (gerado quando omitido)
    ///     e um <paramref name="stateStore" /> opcional para persistir o progresso. A instância é de uso único:
    ///     adicione steps e chame <see cref="ExecuteAsync" /> uma vez.
    /// </summary>
    /// <param name="logger">Logger usado em toda a execução, compensação e descarte.</param>
    /// <param name="sagaId">Identificador da execução; quando nulo, um novo <see cref="Guid" /> é gerado.</param>
    /// <param name="stateStore">Armazenamento opcional para persistir início, conclusão de steps e compensação.</param>
    public SagaOrchestrator(
        ILogger<SagaOrchestrator<TContext>> logger,
        Guid? sagaId = null,
        ISagaStateStore? stateStore = null) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        SagaId = sagaId ?? Guid.NewGuid();
        _stateStore = stateStore;
    }

    /// <summary>Identificador único desta execução de saga, usado em logs e na persistência de estado.</summary>
    public Guid SagaId { get; }

    /// <summary>
    ///     Adiciona uma step ao final da sequência. Só pode ser chamado antes de <see cref="ExecuteAsync" />;
    ///     adicionar steps após o início da execução lança <see cref="InvalidOperationException" />. Retorna a
    ///     própria saga para encadeamento fluente.
    /// </summary>
    public ISaga<TContext> AddStep(ISagaStep<TContext> step) {
        if (step == null) throw new ArgumentNullException(nameof(step));

        if (_isExecuted) throw new InvalidOperationException("Cannot add steps after saga execution has started");

        _steps.Add(step);

        _logger.LogDebug(
            "Step {StepName} added to Saga {SagaId}. Total steps: {StepCount}",
            step.StepName,
            SagaId,
            _steps.Count);

        return this;
    }

    /// <summary>
    ///     Executa as steps na ordem em que foram adicionadas. Se uma step falha (ou lança), compensa todas as
    ///     já executadas em ordem reversa antes de propagar a falha como <see cref="SagaExecutionException" />.
    ///     Só pode ser chamado uma vez e exige ao menos uma step. Em sucesso, lembre-se de chamar
    ///     <see cref="CompleteAsync" /> para suprimir a auto-compensação no descarte.
    /// </summary>
    public async Task<SagaExecutionResult> ExecuteAsync(TContext context, CancellationToken ct = default) {
        if (_isExecuted) throw new InvalidOperationException("Saga has already been executed");

        if (!_steps.Any()) throw new InvalidOperationException("Cannot execute saga without steps");

        _isExecuted = true;

        _logger.LogDebug(
            "Starting Saga {SagaId} (type: {SagaType}) with {StepCount} steps",
            SagaId,
            context.SagaType,
            _steps.Count);

        if (_stateStore != null) await _stateStore.SaveSagaStartAsync(SagaId, context.SagaType, ct);

        try {
            foreach (var step in _steps) {
                _logger.LogDebug("Executing step: {StepName}", step.StepName);

                var stopwatch = Stopwatch.StartNew();
                var result = await step.ExecuteAsync(context, ct);
                stopwatch.Stop();

                if (!result.Success) {
                    var errorMsg = $"Step {step.StepName} failed: {result.ErrorMessage}";
                    _logger.LogError(result.Exception, errorMsg);

                    var stepsExecutedCount = _executedSteps.Count;

                    var compensatedCount = await CompensateExecutedStepsAsync(context, ct);

                    _executionResult = SagaExecutionResult.Failed(
                        errorMsg,
                        stepsExecutedCount,
                        compensatedCount,
                        result.Exception);

                    if (_stateStore != null) await _stateStore.MarkAsCompensatedAsync(SagaId, compensatedCount, ct);

                    throw new SagaExecutionException(
                        SagaId,
                        errorMsg,
                        step.StepName,
                        result.Exception);
                }

                _logger.LogDebug(
                    "Step {StepName} completed successfully in {ElapsedMs}ms",
                    step.StepName,
                    stopwatch.ElapsedMilliseconds);

                _executedSteps.Push((step, result));

                if (_stateStore != null)
                    await _stateStore.SaveStepCompletionAsync(SagaId, step.StepName, result.Data, ct);
            }

            _executionResult = SagaExecutionResult.Successful(_executedSteps.Count);

            _logger.LogDebug(
                "Saga {SagaId} executed successfully. {StepCount} steps completed",
                SagaId,
                _executedSteps.Count);

            return _executionResult;
        }
        catch (SagaExecutionException) {
            throw;
        }
        catch (Exception ex) {
            var stepsExecutedCount = _executedSteps.Count;

            _logger.LogError(
                ex,
                "Saga {SagaId} execution failed with unexpected exception. {StepCount} steps executed. Starting compensation...",
                SagaId,
                stepsExecutedCount);

            var compensatedCount = await CompensateExecutedStepsAsync(context, ct);

            _executionResult = SagaExecutionResult.Failed(
                ex.Message,
                stepsExecutedCount,
                compensatedCount,
                ex);

            if (_stateStore != null) await _stateStore.MarkAsCompensatedAsync(SagaId, compensatedCount, ct);

            throw;
        }
    }

    /// <summary>
    ///     Marca a saga como concluída com sucesso, impedindo a auto-compensação no descarte. Deve ser chamado
    ///     após um <see cref="ExecuteAsync" /> bem-sucedido; é idempotente (chamadas repetidas apenas logam um
    ///     aviso) e exige que a saga já tenha sido executada.
    /// </summary>
    public async Task CompleteAsync(CancellationToken ct = default) {
        if (!_isExecuted) throw new InvalidOperationException("Cannot complete saga before execution");

        if (_isCompleted) {
            _logger.LogWarning("Saga {SagaId} already completed", SagaId);
            return;
        }

        _isCompleted = true;

        _logger.LogDebug("Saga {SagaId} marked as completed", SagaId);

        if (_stateStore != null) await _stateStore.MarkAsCompletedAsync(SagaId, ct);
    }

    /// <summary>
    ///     Rede de segurança: se a saga foi executada mas não recebeu <see cref="CompleteAsync" />, dispara a
    ///     compensação automática durante o descarte. Falhas na compensação são logadas como críticas e nunca
    ///     propagadas a partir do descarte.
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (_disposed) return;

        try {
            if (_isExecuted && !_isCompleted) {
                _logger.LogWarning(
                    "Saga {SagaId} disposed without calling CompleteAsync(). Starting automatic compensation...",
                    SagaId);

                var compensatedCount = await CompensateAsync(CancellationToken.None);

                if (_executionResult != null)
                    _executionResult = SagaExecutionResult.Failed(
                        _executionResult.ErrorMessage ?? "Saga disposed without completion",
                        _executionResult.StepsExecuted,
                        compensatedCount,
                        _executionResult.Exception);

                if (_stateStore != null)
                    await _stateStore.MarkAsCompensatedAsync(SagaId, compensatedCount, CancellationToken.None);
            }
        }
        catch (Exception ex) {
            _logger.LogCritical(
                ex,
                "Critical error during Saga {SagaId} disposal/compensation",
                SagaId);
        }
        finally {
            _disposed = true;
        }
    }

    /// <summary>
    ///     Compensa todas as steps já executadas na ordem reversa (LIFO). Chamado automaticamente quando uma
    ///     step falha. Se a compensação de uma step lança, o erro é logado como crítico e a compensação das
    ///     demais continua, para maximizar a reversão antes de exigir intervenção manual.
    /// </summary>
    private async Task<int> CompensateExecutedStepsAsync(TContext context, CancellationToken ct) {
        var compensatedCount = 0;

        if (_executedSteps.Count == 0) {
            _logger.LogDebug("No steps to compensate for Saga {SagaId}", SagaId);
            return compensatedCount;
        }

        _logger.LogWarning(
            "Starting compensation for Saga {SagaId}. {StepCount} steps to compensate",
            SagaId,
            _executedSteps.Count);

        while (_executedSteps.Count > 0) {
            var (step, result) = _executedSteps.Pop();

            try {
                _logger.LogWarning("Compensating step: {StepName}", step.StepName);

                var stopwatch = Stopwatch.StartNew();
                await step.CompensateAsync(context, result, ct);
                stopwatch.Stop();

                compensatedCount++;

                _logger.LogDebug(
                    "Step {StepName} compensated successfully in {ElapsedMs}ms",
                    step.StepName,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception compensationEx) {
                _logger.LogCritical(
                    compensationEx,
                    "CRITICAL: Compensation failed for step {StepName} in Saga {SagaId}. Manual intervention required!",
                    step.StepName,
                    SagaId);
            }
        }

        _logger.LogWarning(
            "Compensation completed for Saga {SagaId}. {CompensatedCount} steps compensated",
            SagaId,
            compensatedCount);

        return compensatedCount;
    }

    /// <summary>
    ///     Método privado usado pelo DisposeAsync como fallback de segurança.
    ///     Nota: Este método não tem acesso ao contexto, então não pode compensar.
    ///     A compensação principal já foi feita no ExecuteAsync quando um step falha.
    ///     Se há steps restantes aqui, indica um problema no fluxo de execução.
    /// </summary>
    private Task<int> CompensateAsync(CancellationToken ct) {
        if (_executedSteps.Count > 0)
            _logger.LogCritical(
                "CRITICAL: Saga {SagaId} disposed with {StepCount} uncompensated steps. " +
                "This indicates the saga failed but compensation was not called properly. " +
                "Manual intervention may be required.",
                SagaId,
                _executedSteps.Count);

        return Task.FromResult(0);
    }
}