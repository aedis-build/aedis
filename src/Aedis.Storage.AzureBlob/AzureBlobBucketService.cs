using System.Runtime.CompilerServices;
using Aedis.Storage.Abstractions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Aedis.Storage.AzureBlob;

/// <summary>
///     Provider Azure Blob Storage de <see cref="IBucket{T}" />. Cada instância controla um container fixo;
///     o tratamento de stream/memória e o progresso vêm do <see cref="BucketServiceBase{T}" />.
///     Crie um marcador por container: <c>class Invoices : AzureBlobBucketService&lt;Invoices&gt; { ... }</c>.
/// </summary>
public abstract class AzureBlobBucketService<T> : BucketServiceBase<T>
{
    private readonly BlobContainerClient _container;
    private readonly string _prefix;

    /// <summary>
    ///     Cria o serviço a partir das <see cref="AzureBlobStorageOptions" />, resolvendo o
    ///     <see cref="BlobContainerClient" /> via ConnectionString e fixando o container e o prefixo desta instância.
    /// </summary>
    /// <param name="options">Configuração do container e do acesso Azure (ConnectionString e ContainerName obrigatórios).</param>
    protected AzureBlobBucketService(AzureBlobStorageOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("ConnectionString é obrigatória.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.ContainerName))
            throw new ArgumentException("ContainerName é obrigatório.", nameof(options));

        _container = new BlobServiceClient(options.ConnectionString).GetBlobContainerClient(options.ContainerName);
        _prefix = (options.Prefix ?? string.Empty).Trim('/');
    }

    /// <summary>Construtor para injeção de um <see cref="BlobContainerClient" /> já configurado (útil em testes).</summary>
    protected AzureBlobBucketService(BlobContainerClient container, string prefix = "") {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _prefix = (prefix ?? string.Empty).Trim('/');
    }

    /// <inheritdoc />
    protected override async Task<ObjectContent?> OpenObjectAsync(string key, CancellationToken cancellationToken) {
        var blob = _container.GetBlobClient(ResolveKey(key));
        try {
            var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return new ObjectContent(response.Value.Content, response.Value.Details.ContentLength);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) {
            return null;
        }
    }

    /// <inheritdoc />
    protected override async Task UploadObjectAsync(string key, Stream stream, string contentType,
        long? contentLength, CancellationToken cancellationToken) {
        var blob = _container.GetBlobClient(ResolveKey(key));
        var options = new BlobUploadOptions {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };
        await blob.UploadAsync(stream, options, cancellationToken);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<BucketObject> ListObjectsAsync(string? prefix, long offsetTimestamp = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var fullPrefix = Combine(_prefix, prefix);
        var offset = offsetTimestamp != 0 ? DateTimeOffset.FromUnixTimeMilliseconds(offsetTimestamp) : (DateTimeOffset?)null;

        await foreach (var item in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, fullPrefix, cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Name.EndsWith('/')) continue;

            var modified = item.Properties.LastModified ?? DateTimeOffset.UtcNow;
            if (offset is not null && modified <= offset.Value) continue;

            yield return new BucketObject(_container.Name, item.Name, modified);
        }
    }

    /// <inheritdoc />
    public override Task DeleteObjectAsync(string key, CancellationToken cancellationToken = default) {
        return _container.GetBlobClient(ResolveKey(key)).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public override async Task CopyObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default) {
        var src = ResolveKey(sourceKey);
        var dst = ResolveKey(destinationKey);
        if (string.Equals(src, dst, StringComparison.Ordinal)) return;

        var source = _container.GetBlobClient(src);
        var destination = _container.GetBlobClient(dst);
        await destination.StartCopyFromUriAsync(source.Uri, new BlobCopyFromUriOptions(), cancellationToken);
    }

    /// <inheritdoc />
    public override Task<string> GetPreSignedUrlAsync(string key, TimeSpan ttl,
        FileAccess accessType = FileAccess.Read, CancellationToken cancellationToken = default) {
        var resolved = ResolveKey(key);
        var blob = _container.GetBlobClient(resolved);

        var sas = new BlobSasBuilder {
            BlobContainerName = _container.Name,
            BlobName = resolved,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(ttl)
        };
        sas.SetPermissions(ToPermissions(accessType));

        return Task.FromResult(blob.GenerateSasUri(sas).ToString());
    }

    private string ResolveKey(string key) {
        var normalized = key.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(_prefix) ? normalized : $"{_prefix}/{normalized}";
    }

    private static string? Combine(string prefix, string? sub) {
        var normalizedSub = sub?.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(prefix)) return string.IsNullOrEmpty(normalizedSub) ? null : normalizedSub;
        return string.IsNullOrEmpty(normalizedSub) ? prefix : $"{prefix}/{normalizedSub}";
    }

    private static BlobSasPermissions ToPermissions(FileAccess accessType) {
        return accessType switch {
            FileAccess.Read => BlobSasPermissions.Read,
            FileAccess.Write => BlobSasPermissions.Write | BlobSasPermissions.Create,
            FileAccess.ReadWrite => BlobSasPermissions.Read | BlobSasPermissions.Write | BlobSasPermissions.Create,
            _ => throw new ArgumentOutOfRangeException(nameof(accessType), accessType, "Tipo de acesso não suportado")
        };
    }
}
