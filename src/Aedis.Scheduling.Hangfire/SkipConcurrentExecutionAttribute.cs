using Hangfire.Common;
using Hangfire.Server;
using Hangfire.Storage;

namespace Aedis.Scheduling.Hangfire;

/// <summary>
///     Descarta silenciosamente um job quando outro do mesmo tipo já está em execução — o disparo vai para
///     <c>Deleted</c> (sem erro, sem retry, sem ruído no dashboard). Diferente do
///     <c>DisableConcurrentExecution</c>, que aguarda o lock e falha (<c>Failed</c>) se expirar.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SkipConcurrentExecutionAttribute : JobFilterAttribute, IServerFilter
{
    private const string LockKey = "SkipConcurrentLock";

    /// <summary>
    ///     Antes do job rodar, tenta adquirir um lock distribuído nomeado pelo tipo+método sem esperar
    ///     (<see cref="TimeSpan.Zero" />). Se o lock já está tomado, marca o contexto como cancelado para que
    ///     o disparo seja descartado em vez de aguardar.
    /// </summary>
    public void OnPerforming(PerformingContext context) {
        var resource = $"{context.BackgroundJob.Job.Type.FullName}.{context.BackgroundJob.Job.Method.Name}";

        try {
            context.Items[LockKey] = context.Connection.AcquireDistributedLock(resource, TimeSpan.Zero);
        }
        catch (DistributedLockTimeoutException) {
            context.Canceled = true;
        }
    }

    /// <summary>Após o job terminar, libera o lock distribuído adquirido em <see cref="OnPerforming" />.</summary>
    public void OnPerformed(PerformedContext context) {
        if (context.Items.TryGetValue(LockKey, out var lockObj) && lockObj is IDisposable disposable)
            disposable.Dispose();
    }
}
