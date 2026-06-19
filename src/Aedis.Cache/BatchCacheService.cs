using Aedis.Cache.Abstractions;
using Aedis.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Aedis.Cache;

/// <summary>
///     Orquestra o processamento idempotente de um lote sobre qualquer <see cref="ICache" />: elege o
///     líder do lote (exclusividade), guarda/recupera o checkpoint de linha para retomada, deduplica
///     itens já processados e contabiliza o progresso. Implementação canônica e agnóstica de provider —
///     usa apenas as primitivas do contrato <see cref="ICache" />.
/// </summary>
public sealed class BatchCacheService(ICache cache, ILogger<BatchCacheService> logger) : IBatchCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(8);

    /// <inheritdoc />
    public async Task<IBatchCheckpoint?> GetCheckpointAsync(string batchId, TimeSpan expiration,
        CancellationToken cancellationToken = default) {
        var lockHandle = await cache.IsLeaderAsync(batchId, expiration, cancellationToken);

        if (lockHandle is null) {
            logger.LogDebug("Lote {BatchId} já está sob outro líder — sem checkpoint.", batchId);
            return null;
        }

        var checkpointValue = await cache.GetStringAsync(FormatCheckpointKey(batchId), cancellationToken);
        var checkpoint = 0;

        if (!string.IsNullOrEmpty(checkpointValue) && int.TryParse(checkpointValue, out var parsed))
            checkpoint = parsed;

        logger.LogDebug("Lote {BatchId} retomado na linha {Checkpoint}.", batchId, checkpoint);
        return new BatchCheckpoint(checkpoint, lockHandle);
    }

    /// <inheritdoc />
    public Task UpdateCheckpointAsync(string batchId, int line, CancellationToken cancellationToken = default) {
        return cache.SetStringAsync(FormatCheckpointKey(batchId), line.ToString(), DefaultTtl, cancellationToken);
    }

    /// <summary>
    ///     Marca um item do lote como processado e indica se a marcação é nova (deduplicação real via
    ///     <see cref="ICache.SetIfNotExistsAsync" /> por item — devolve <c>false</c> se já estava marcado).
    /// </summary>
    public async Task<bool> MarkProcessedAsync(string batchId, string uuid,
        CancellationToken cancellationToken = default) {
        var wasNewlyMarked = await cache.SetIfNotExistsAsync(
            FormatProcessedKey(batchId, uuid), DateTimeOffset.UtcNow.Ticks.ToString(), DefaultTtl, cancellationToken);

        if (wasNewlyMarked)
            logger.LogTrace("Item {Uuid} do lote {BatchId} marcado como processado.", uuid, batchId);
        else
            logger.LogTrace("Item {Uuid} do lote {BatchId} já estava processado (ignorado).", uuid, batchId);

        return wasNewlyMarked;
    }

    /// <inheritdoc />
    public async Task IncrementProgressAsync(string batchId, CancellationToken cancellationToken = default) {
        await cache.IncrementAsync(FormatProgressKey(batchId), DefaultTtl, cancellationToken);
    }

    private static string FormatCheckpointKey(string batchId) =>
        $"{ApplicationInfo.Name}:{batchId}:checkpoint".ToLowerInvariant();

    private static string FormatProcessedKey(string batchId, string uuid) =>
        $"{ApplicationInfo.Name}:{batchId}:processed:{uuid}".ToLowerInvariant();

    private static string FormatProgressKey(string batchId) =>
        $"{ApplicationInfo.Name}:{batchId}:progress".ToLowerInvariant();
}
