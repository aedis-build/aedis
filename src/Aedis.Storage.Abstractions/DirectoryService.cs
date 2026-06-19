using System.Runtime.CompilerServices;
using Aedis.Domain.Strategy.Abstractions;
using Aedis.Storage.Abstractions.Streaming;

namespace Aedis.Storage.Abstractions;

/// <summary>
///     Implementação <em>FileSystem</em> default de <see cref="IDirectory" />: cada instância controla
///     <strong>uma pasta local</strong> informada no construtor. Crie um <see cref="DirectoryService" />
///     por pasta para isolar o controle. Stateless e thread-safe (pode ser singleton por pasta).
///     <para>
///         Reusa a maquinaria de stream/memória da plataforma (Memory/TempFile/Chunked) e protege contra
///         <em>path traversal</em>: nenhuma chave escapa da pasta base.
///     </para>
/// </summary>
public sealed class DirectoryService : IDirectory
{
    private const int BufferSize = 1024 * 1024;

    private readonly string _basePath;
    private readonly IStrategyResolver<StreamContext> _streamResolver = StreamStrategyResolver.CreateDefault();

    /// <param name="basePath">Caminho base da pasta local controlada por esta instância (obrigatório).</param>
    public DirectoryService(string basePath) {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentException("O caminho base da pasta é obrigatório.", nameof(basePath));

        _basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));
        Directory.CreateDirectory(_basePath);
    }

    /// <summary>Caminho base (absoluto) da pasta controlada.</summary>
    public string BasePath => _basePath;

    /// <inheritdoc />
    public async IAsyncEnumerable<BucketObject> ListObjectsAsync(string? prefix, long offsetTimestamp = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var searchPath = GetFullPath(prefix ?? string.Empty);
        if (!Directory.Exists(searchPath)) yield break;

        var option = string.IsNullOrEmpty(prefix) ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var offset = offsetTimestamp != 0 ? DateTimeOffset.FromUnixTimeMilliseconds(offsetTimestamp) : (DateTimeOffset?)null;

        foreach (var filePath in Directory.EnumerateFiles(searchPath, "*", option)) {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = GetRelativePath(filePath);
            if (string.IsNullOrEmpty(relativePath)) continue;

            var lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
            if (offset is not null && lastModified <= offset.Value) continue;

            yield return new BucketObject(_basePath, relativePath, lastModified);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Abre o arquivo e aplica a estratégia de stream/memória do <paramref name="mode" />; retorna
    ///     <c>null</c> se o objeto não existir.
    /// </summary>
    /// <remarks>
    ///     Nos modos Memory/TempFile a estratégia copia o conteúdo para um novo stream; quando isso ocorre
    ///     (o resultado deixa de ser o <see cref="FileStream" /> de origem), o FileStream original é liberado
    ///     para não vazar o handle do arquivo.
    /// </remarks>
    public async Task<Stream?> GetObjectAsync(string key, StreamMode mode = StreamMode.Default,
        CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(key);
        if (!File.Exists(fullPath)) return null;

        var length = new FileInfo(fullPath).Length;
        var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);

        var context = new StreamContext { Mode = mode, SourceStream = fileStream, ContentLength = length };
        await _streamResolver.ExecuteAsync(context, cancellationToken);

        if (!ReferenceEquals(context.Result, fileStream))
            await fileStream.DisposeAsync();

        return context.Result;
    }

    /// <inheritdoc />
    public async Task PutObjectAsync(string key, Stream stream, string contentType = "application/octet-stream",
        Action<UploadProgress>? onProgress = null, long? contentLength = null,
        CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(key);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var fileStream =
            new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        if (onProgress is null) {
            await stream.CopyToAsync(fileStream, BufferSize, cancellationToken);
            return;
        }

        var total = contentLength ?? (stream.CanSeek ? stream.Length : 0);
        long transferred = 0;
        var buffer = new byte[BufferSize];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0) {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            transferred += read;
            var percent = total > 0 ? (int)(transferred * 100 / total) : 0;
            onProgress(new UploadProgress(key, transferred, total, percent));
        }
    }

    /// <inheritdoc />
    public async Task CopyObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default) {
        var sourcePath = GetFullPath(sourceKey);
        var destPath = GetFullPath(destinationKey);
        if (string.Equals(sourcePath, destPath, StringComparison.Ordinal)) return;

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Objeto de origem não encontrado: {sourceKey}", sourcePath);

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

        await Task.Run(() => File.Copy(sourcePath, destPath, true), cancellationToken);
    }

    /// <inheritdoc />
    public async Task MoveObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default) {
        await CopyObjectAsync(sourceKey, destinationKey, cancellationToken);
        await DeleteObjectAsync(sourceKey, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteObjectAsync(string key, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(key);
        if (File.Exists(fullPath)) await Task.Run(() => File.Delete(fullPath), cancellationToken);
    }

    /// <summary>
    ///     Resolve a chave para um caminho absoluto dentro da pasta base, bloqueando <em>path traversal</em>:
    ///     chaves que escapem da base (via <c>..</c>) resultam em <see cref="UnauthorizedAccessException" />.
    /// </summary>
    private string GetFullPath(string key) {
        var normalizedKey = key.Replace('\\', '/').Trim('/');
        var resolved = Path.GetFullPath(Path.Combine(_basePath, normalizedKey));

        if (resolved != _basePath &&
            !resolved.StartsWith(_basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"Acesso negado: '{key}' está fora da pasta base.");

        return resolved;
    }

    private string GetRelativePath(string fullPath) {
        var resolved = Path.GetFullPath(fullPath);
        if (resolved != _basePath &&
            !resolved.StartsWith(_basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return string.Empty;

        return Path.GetRelativePath(_basePath, resolved).Replace('\\', '/');
    }
}
