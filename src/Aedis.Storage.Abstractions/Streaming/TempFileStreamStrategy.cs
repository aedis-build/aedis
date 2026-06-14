using Aedis.Domain.Strategy.Abstractions;

namespace Aedis.Storage.Abstractions.Streaming;

/// <summary>
///     Faz spool do objeto para um arquivo temporário (deletado ao fechar — <see cref="FileOptions.DeleteOnClose" />),
///     evitando pressão de memória em payloads grandes. Cobre o modo TempFile e o Memory acima do threshold.
/// </summary>
public sealed class TempFileStreamStrategy : IStrategy<StreamContext>
{
    private const int BufferSize = 1024 * 1024;

    public bool CanHandle(StreamContext context) {
        return context.Mode == StreamMode.TempFile ||
               context is { Mode: StreamMode.Memory, ContentLength: > MemoryStreamStrategy.MemoryThreshold };
    }

    public async Task ExecuteAsync(StreamContext context, CancellationToken cancellationToken = default) {
        var tempFile = Path.GetTempFileName();
        var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize,
            FileOptions.DeleteOnClose);
        await context.SourceStream.CopyToAsync(fileStream, BufferSize, cancellationToken);
        fileStream.Position = 0;
        context.Result = fileStream;
    }
}
