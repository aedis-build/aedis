using Aedis.Scheduling.Abstractions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Scheduling.Hangfire;

/// <summary>
///     Wrapper genérico que executa um <see cref="ICronJobProcessor" /> via Hangfire, mantendo os
///     processors <strong>sem qualquer dependência do Hangfire</strong>. A cada disparo cria um escopo de
///     DI novo, resolve o processor e chama <c>ProcessAsync</c>.
///     <para>
///         <c>[SkipConcurrentExecution]</c>: descarta silenciosamente um novo disparo se já há uma
///         execução em andamento (vai para <c>Deleted</c>, sem erro nem retry).
///         <c>[AutomaticRetry(Attempts = 0)]</c>: sem re-enfileiramento automático.
///     </para>
/// </summary>
public sealed class CronJobExecutor<TProcessor>(IServiceScopeFactory scopeFactory)
    where TProcessor : class, ICronJobProcessor
{
    [SkipConcurrentExecution]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken = default) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<TProcessor>();
        await processor.ProcessAsync(cancellationToken);
    }
}
