namespace Aedis.Storage.Abstractions;

/// <summary>
///     Contrato agnóstico de provider para um bucket de armazenamento de objetos.
///     O parâmetro <typeparamref name="T" /> é apenas um marcador que distingue buckets
///     diferentes no contêiner de DI — não referencia nenhuma implementação.
/// </summary>
public interface IBucket<T>
{
    /// <summary>
    ///     Lista objetos do bucket de forma assíncrona, opcionalmente filtrando por prefixo e por data
    ///     de modificação.
    /// </summary>
    /// <param name="prefix">Prefixo para filtrar as chaves. Se null, lista todas.</param>
    /// <param name="offsetTimestamp">Unix ms; apenas objetos modificados após este timestamp. Use 0 para todos.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    IAsyncEnumerable<BucketObject> ListObjectsAsync(string? prefix, long offsetTimestamp = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Obtém um objeto como Stream (ou null se não encontrado), aplicando o tratamento de
    ///     stream/memória indicado por <paramref name="mode" />.
    /// </summary>
    /// <param name="key">Chave do objeto.</param>
    /// <param name="mode">Modo de stream (Default, Memory, TempFile ou Chunked).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<Stream?> GetObjectAsync(string key, StreamMode mode = StreamMode.Default,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Envia um objeto para o bucket, reportando progresso via <paramref name="onProgress" /> quando fornecido.
    /// </summary>
    /// <param name="key">Chave do objeto.</param>
    /// <param name="stream">Stream com os dados.</param>
    /// <param name="contentType">Tipo de conteúdo. Padrão: "application/octet-stream".</param>
    /// <param name="onProgress">Callback opcional de progresso.</param>
    /// <param name="contentLength">Tamanho em bytes; se null, calculado quando possível.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task PutObjectAsync(string key, Stream stream, string contentType = "application/octet-stream",
        Action<UploadProgress>? onProgress = null, long? contentLength = null,
        CancellationToken cancellationToken = default);

    /// <summary>Exclui um objeto do bucket. Não falha se a chave não existir.</summary>
    Task DeleteObjectAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Copia um objeto dentro do mesmo bucket, preservando a origem.</summary>
    Task CopyObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default);

    /// <summary>Move um objeto dentro do mesmo bucket (copia e exclui a origem).</summary>
    Task MoveObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gera uma URL de acesso temporário ao objeto (presigned URL / SAS, conforme o provider).
    /// </summary>
    /// <param name="key">Chave do objeto.</param>
    /// <param name="ttl">Tempo de validade da URL.</param>
    /// <param name="accessType">Tipo de acesso concedido pela URL (padrão: leitura).</param>
    /// <param name="cancellationToken">Token de cancelamento da operação.</param>
    Task<string> GetPreSignedUrlAsync(string key, TimeSpan ttl,
        FileAccess accessType = FileAccess.Read,
        CancellationToken cancellationToken = default);
}
