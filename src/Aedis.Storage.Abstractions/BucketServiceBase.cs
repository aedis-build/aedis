using Aedis.Domain.Strategy.Abstractions;
using Aedis.Storage.Abstractions.Streaming;

namespace Aedis.Storage.Abstractions;

/// <summary>
///     Base <em>Template Method</em> para implementações de <see cref="IBucket{T}" />.
///     A plataforma cuida do tratamento correto de stream/memória (Memory/TempFile/Chunked) e do
///     reporte de progresso de upload; o provider concreto só implementa as chamadas específicas do
///     SDK (abrir/enviar objeto, listar, excluir, copiar, URL temporária).
/// </summary>
/// <typeparam name="T">Marcador que distingue buckets no contêiner de DI.</typeparam>
public abstract class BucketServiceBase<T> : IBucket<T>
{
    private readonly IStrategyResolver<StreamContext> _streamResolver = StreamStrategyResolver.CreateDefault();

    // ---------- Template methods (lógica comum, agnóstica de provider) ----------

    /// <summary>
    ///     Obtém o objeto aplicando a estratégia de stream/memória do <paramref name="mode" />.
    ///     O provider apenas abre o stream bruto via <see cref="OpenObjectAsync" />.
    /// </summary>
    public async Task<Stream?> GetObjectAsync(string key, StreamMode mode = StreamMode.Default,
        CancellationToken cancellationToken = default) {
        var content = await OpenObjectAsync(key, cancellationToken);
        if (content is null) return null;

        var context = new StreamContext {
            Mode = mode,
            SourceStream = content.Value.Stream,
            ContentLength = content.Value.Length
        };

        await _streamResolver.ExecuteAsync(context, cancellationToken);
        return context.Result;
    }

    /// <summary>
    ///     Envia o objeto, reportando progresso (via <c>ProgressStream</c>) quando
    ///     <paramref name="onProgress" /> é fornecido. O upload em si é delegado ao provider.
    /// </summary>
    public Task PutObjectAsync(string key, Stream stream, string contentType = "application/octet-stream",
        Action<UploadProgress>? onProgress = null, long? contentLength = null,
        CancellationToken cancellationToken = default) {
        if (onProgress is null)
            return UploadObjectAsync(key, stream, contentType, contentLength, cancellationToken);

        long transferred = 0;
        var total = contentLength ?? (stream.CanSeek ? stream.Length : 0);

        var progressStream = new ProgressStream(stream, bytes => {
            transferred += bytes;
            var percent = total > 0 ? (int)(transferred * 100 / total) : 0;
            onProgress(new UploadProgress(key, transferred, total, percent));
        });

        return UploadObjectAsync(key, progressStream, contentType, contentLength, cancellationToken);
    }

    /// <summary>Move = copiar e excluir a origem (comum a todos os providers).</summary>
    public async Task MoveObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default) {
        await CopyObjectAsync(sourceKey, destinationKey, cancellationToken);
        await DeleteObjectAsync(sourceKey, cancellationToken);
    }

    // ---------- Passos específicos do provider (preenchidos pela implementação) ----------

    /// <summary>
    ///     Abre o objeto para leitura, retornando o stream bruto e o tamanho — ou <c>null</c> se não existir.
    /// </summary>
    protected abstract Task<ObjectContent?> OpenObjectAsync(string key, CancellationToken cancellationToken);

    /// <summary>Envia o stream ao provider de armazenamento.</summary>
    protected abstract Task UploadObjectAsync(string key, Stream stream, string contentType,
        long? contentLength, CancellationToken cancellationToken);

    public abstract IAsyncEnumerable<BucketObject> ListObjectsAsync(string? prefix, long offsetTimestamp = 0,
        CancellationToken cancellationToken = default);

    public abstract Task DeleteObjectAsync(string key, CancellationToken cancellationToken = default);

    public abstract Task CopyObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default);

    public abstract Task<string> GetPreSignedUrlAsync(string key, TimeSpan ttl,
        FileAccess accessType = FileAccess.Read, CancellationToken cancellationToken = default);

    /// <summary>Stream bruto de um objeto e seu tamanho, retornado por <see cref="OpenObjectAsync" />.</summary>
    protected readonly record struct ObjectContent(Stream Stream, long Length);
}
