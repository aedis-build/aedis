using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Aedis.Domain.Saga.Abstractions;

namespace Aedis.Domain.Saga;

/// <summary>
///     Orquestrador de saga que gerencia execução sequencial de steps
///     e compensação automática em caso de falha.
/// </summary>
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

    public SagaOrchestrator(
        ILogger<SagaOrchestrator<TContext>> logger,
        Guid? sagaId = null,
        ISagaStateStore? stateStore = null) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        SagaId = sagaId ?? Guid.NewGuid();
        _stateStore = stateStore;
    }

    public Guid SagaId { get; }

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

                    // Guarda contagem antes de compensar (compensação vai esvaziar a stack)
                    var stepsExecutedCount = _executedSteps.Count;

                    // Compensa todas as steps já executadas antes de lançar exceção
                    var compensatedCount = await CompensateExecutedStepsAsync(context, ct);

                    _executionResult = SagaExecutionResult.Failed(
                        errorMsg,
                        stepsExecutedCount, // Contagem antes da compensação
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
            // Re-throw SagaExecutionException (já compensou antes de lançar)
            throw;
        }
        catch (Exception ex) {
            // Guarda contagem antes de compensar (compensação vai esvaziar a stack)
            var stepsExecutedCount = _executedSteps.Count;

            _logger.LogError(
                ex,
                "Saga {SagaId} execution failed with unexpected exception. {StepCount} steps executed. Starting compensation...",
                SagaId,
                stepsExecutedCount);

            // Compensa steps executadas em caso de exceção inesperada
            var compensatedCount = await CompensateExecutedStepsAsync(context, ct);

            _executionResult = SagaExecutionResult.Failed(
                ex.Message,
                stepsExecutedCount, // Contagem antes da compensação
                compensatedCount,
                ex);

            if (_stateStore != null) await _stateStore.MarkAsCompensatedAsync(SagaId, compensatedCount, ct);

            throw;
        }
    }

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
    ///     Compensa todas as steps já executadas na ordem reversa (LIFO).
    ///     Chamado automaticamente quando uma step falha.
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
                // Continua compensando outras steps mesmo se uma falhar
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

        // Não podemos compensar sem contexto
        // A compensação já deve ter sido feita no ExecuteAsync
        return Task.FromResult(0);
    }
}