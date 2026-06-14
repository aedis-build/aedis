namespace Aedis.Cache.Abstractions;

public interface IBatchCache
{
    Task<IBatchCheckpoint?> GetCheckpointAsync(string batchId, TimeSpan expiration, CancellationToken ct = default);
    Task UpdateCheckpointAsync(string batchId, int line, CancellationToken cancellationToken = default);
    Task<bool> MarkProcessedAsync(string batchId, string uuid, CancellationToken cancellationToken = default);
    Task IncrementProgressAsync(string batchId, CancellationToken ct = default);
}