namespace Aedis.Cache.Abstractions;

public interface IBatchCheckpoint : IAsyncDisposable
{
    int Checkpoint { get; }
}