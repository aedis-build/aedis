using Aedis.Domain.Strategy.Abstractions;

namespace Aedis.Storage.Abstractions.Streaming;

/// <summary>
///     Passa o stream do provider adiante sem bufferizar (streaming verdadeiro).
///     Padrão para grandes downloads consumidos progressivamente, sem custo de memória.
/// </summary>
public sealed class ChunkedStreamStrategy : IStrategy<StreamContext>
{
    /// <inheritdoc />
    public bool CanHandle(StreamContext context) {
        return context.Mode is StreamMode.Chunked or StreamMode.Default;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(StreamContext context, CancellationToken cancellationToken = default) {
        context.Result = context.SourceStream;
        return Task.CompletedTask;
    }
}
