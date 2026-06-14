namespace Aedis.Storage.Abstractions;

/// <summary>
///     Contrato agnóstico de provider para um bucket de armazenamento de objetos.
///     O parâmetro <typeparamref name="T" /> é apenas um marcador que distingue buckets
///     diferentes no contêiner de DI — não referencia nenhuma implementação.
/// </summary>
public interface IBucket<T>
{
    IAsyncEnumerable<BucketObject> ListObjectsAsync(string? prefix, long offsetTimestamp = 0,
        CancellationToken cancellationToken = default);

    Task<Stream> GetObjectAsync(string key, StreamMode mode = StreamMode.Default,
        CancellationToken cancellationToken = default);

    Task PutObjectAsync(string key, Stream stream, string contentType = "application/octet-stream",
        Action<UploadProgress>? onProgress = null, long? contentLength = null,
        CancellationToken cancellationToken = default);

    Task DeleteObjectAsync(string key, CancellationToken cancellationToken = default);

    Task CopyObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default);

    Task MoveObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default);
}
