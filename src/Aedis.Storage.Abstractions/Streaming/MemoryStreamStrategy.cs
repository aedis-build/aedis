using Aedis.Domain.Strategy.Abstractions;

namespace Aedis.Storage.Abstractions.Streaming;

/// <summary>
///     Carrega o objeto inteiro em memória (MemoryStream). Adequado a payloads pequenos
///     (até <see cref="MemoryThreshold" />); acima disso, o TempFile assume.
/// </summary>
public sealed class MemoryStreamStrategy : IStrategy<StreamContext>
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>
    ///     Limite (8 MiB) acima do qual o modo Memory deixa de bufferizar em RAM e cede ao spool em arquivo
    ///     temporário (<see cref="TempFileStreamStrategy" />), evitando pressão de memória.
    /// </summary>
    public const long MemoryThreshold = 8 * 1024 * 1024;

    /// <inheritdoc />
    public bool CanHandle(StreamContext context) {
        return context is { Mode: StreamMode.Memory, ContentLength: <= MemoryThreshold };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(StreamContext context, CancellationToken cancellationToken = default) {
        var memoryStream = new MemoryStream((int)context.ContentLength);
        await context.SourceStream.CopyToAsync(memoryStream, BufferSize, cancellationToken);
        memoryStream.Position = 0;
        context.Result = memoryStream;
    }
}
