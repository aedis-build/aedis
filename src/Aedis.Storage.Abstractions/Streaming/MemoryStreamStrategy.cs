using Aedis.Domain.Strategy.Abstractions;

namespace Aedis.Storage.Abstractions.Streaming;

/// <summary>
///     Carrega o objeto inteiro em memória (MemoryStream). Adequado a payloads pequenos
///     (até <see cref="MemoryThreshold" />); acima disso, o TempFile assume.
/// </summary>
public sealed class MemoryStreamStrategy : IStrategy<StreamContext>
{
    private const int BufferSize = 1024 * 1024;
    public const long MemoryThreshold = 8 * 1024 * 1024;

    public bool CanHandle(StreamContext context) {
        return context is { Mode: StreamMode.Memory, ContentLength: <= MemoryThreshold };
    }

    public async Task ExecuteAsync(StreamContext context, CancellationToken cancellationToken = default) {
        var memoryStream = new MemoryStream((int)context.ContentLength);
        await context.SourceStream.CopyToAsync(memoryStream, BufferSize, cancellationToken);
        memoryStream.Position = 0;
        context.Result = memoryStream;
    }
}
