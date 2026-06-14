namespace Aedis.Cache.Abstractions;

public interface IExecutionCacheContext : IAsyncDisposable
{
    Task<DateTimeOffset?> GetLastExecution(CancellationToken cancellationToken = default);

    Task<bool> MarkAsProcessedAsync(string value, CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}