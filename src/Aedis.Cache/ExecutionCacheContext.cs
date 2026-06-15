using Aedis.Cache.Abstractions;
using Aedis.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Aedis.Cache;

/// <summary>
///     Contexto de uma execução (job/ciclo) sobre qualquer <see cref="ICache" />: expõe o instante da
///     última execução, deduplica itens já processados nesta janela e confirma (commit) o avanço do
///     marcador. Implementa <see cref="IAsyncDisposable" /> e <em>avisa</em> se for descartado sem
///     commit — sinal de que a janela não avançou. Implementação canônica e agnóstica de provider.
/// </summary>
public sealed class ExecutionCacheContext(ICache cache, ILogger<ExecutionCacheContext> logger)
    : IExecutionCacheContext
{
    private bool _committed;

    private static string LastExecutionKey => $"{ApplicationInfo.Name}:last-execution".ToLowerInvariant();

    public async Task<DateTimeOffset?> GetLastExecution(CancellationToken cancellationToken = default) {
        var lastExecStr = await cache.GetStringAsync(LastExecutionKey, cancellationToken);

        if (string.IsNullOrEmpty(lastExecStr) || !long.TryParse(lastExecStr, out var lastExec))
            return null;

        return new DateTimeOffset(lastExec, TimeSpan.Zero);
    }

    public async Task<bool> MarkAsProcessedAsync(string value, CancellationToken cancellationToken = default) {
        var wasNewlyMarked = await cache.SetIfNotExistsAsync(
            FormatProcessedKey(value), DateTimeOffset.UtcNow.Ticks.ToString(), TimeSpan.FromDays(7), cancellationToken);

        if (wasNewlyMarked)
            logger.LogTrace("Item marcado como processado: {Value}", value);
        else
            logger.LogTrace("Item já estava marcado como processado: {Value}", value);

        return wasNewlyMarked;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default) {
        await cache.SetStringAsync(LastExecutionKey, DateTimeOffset.UtcNow.Ticks.ToString(),
            TimeSpan.FromDays(365), cancellationToken);
        _committed = true;
    }

    public ValueTask DisposeAsync() {
        if (!_committed)
            logger.LogWarning("Execução não confirmada (sem commit) — o marcador de última execução não foi salvo.");
        return ValueTask.CompletedTask;
    }

    private static string FormatProcessedKey(string value) =>
        $"{ApplicationInfo.Name}:processed:{value}".ToLowerInvariant();
}
