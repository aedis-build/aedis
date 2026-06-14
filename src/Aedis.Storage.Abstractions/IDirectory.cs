namespace Aedis.Storage.Abstractions;

/// <summary>
///     Contrato de armazenamento em diretório local do sistema de arquivos.
///     Espelha as operações de <see cref="IBucket{T}" />, mas para armazenamento local
///     (sem URL pré-assinada, que não se aplica a filesystem).
/// </summary>
public interface IDirectory
{
    /// <summary>
    ///     Lista objetos do diretório de forma assíncrona.
    /// </summary>
    /// <param name="prefix">Prefixo para filtrar os objetos. Se null, lista todos.</param>
    /// <param name="offsetTimestamp">Unix ms; apenas objetos modificados após este timestamp. Use 0 para todos.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    IAsyncEnumerable<BucketObject> ListObjectsAsync(string? prefix, long offsetTimestamp = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Obtém um objeto do diretório como Stream (ou null se não encontrado).
    /// </summary>
    /// <param name="key">Chave do objeto (caminho relativo ao diretório raiz).</param>
    /// <param name="mode">Modo de stream (Default, Memory, TempFile ou Chunked).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<Stream?> GetObjectAsync(string key, StreamMode mode = StreamMode.Default,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Envia um objeto para o diretório.
    /// </summary>
    /// <param name="key">Chave do objeto (caminho relativo ao diretório raiz).</param>
    /// <param name="stream">Stream com os dados.</param>
    /// <param name="contentType">Tipo de conteúdo. Padrão: "application/octet-stream".</param>
    /// <param name="onProgress">Callback opcional de progresso.</param>
    /// <param name="contentLength">Tamanho em bytes; se null, calculado quando possível.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task PutObjectAsync(string key, Stream stream, string contentType = "application/octet-stream",
        Action<UploadProgress>? onProgress = null, long? contentLength = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Exclui um objeto do diretório.
    /// </summary>
    Task DeleteObjectAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Copia um objeto dentro do mesmo diretório.
    /// </summary>
    Task CopyObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Move um objeto dentro do mesmo diretório (copia e exclui o original).
    /// </summary>
    Task MoveObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default);
}
