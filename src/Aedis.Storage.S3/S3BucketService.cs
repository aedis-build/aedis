using System.Net;
using System.Runtime.CompilerServices;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Aedis.Storage.Abstractions;

namespace Aedis.Storage.S3;

/// <summary>
///     Provider AWS S3 (e S3-compatível) de <see cref="IBucket{T}" />. Cada instância controla um bucket
///     fixo; o tratamento de stream/memória e o progresso vêm do <see cref="BucketServiceBase{T}" />.
///     Crie um marcador por bucket: <c>class Invoices : S3BucketService&lt;Invoices&gt; { ... }</c>.
/// </summary>
public abstract class S3BucketService<T> : BucketServiceBase<T>, IDisposable
{
    private readonly string _bucket;
    private readonly string _prefix;
    private readonly IAmazonS3 _s3;

    /// <summary>
    ///     Cria o serviço a partir das <see cref="S3StorageOptions" />, montando o cliente <see cref="IAmazonS3" />
    ///     (credenciais, ServiceURL ou região) e fixando o bucket e o prefixo desta instância.
    /// </summary>
    /// <param name="options">Configuração do bucket e do acesso AWS (BucketName obrigatório).</param>
    protected S3BucketService(S3StorageOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.BucketName))
            throw new ArgumentException("BucketName é obrigatório.", nameof(options));

        _bucket = options.BucketName;
        _prefix = (options.Prefix ?? string.Empty).Trim('/');
        _s3 = CreateClient(options);
    }

    /// <summary>Construtor para injeção de um <see cref="IAmazonS3" /> já configurado (útil em testes).</summary>
    protected S3BucketService(IAmazonS3 s3, string bucketName, string prefix = "") {
        _s3 = s3 ?? throw new ArgumentNullException(nameof(s3));
        _bucket = !string.IsNullOrWhiteSpace(bucketName)
            ? bucketName
            : throw new ArgumentException("BucketName é obrigatório.", nameof(bucketName));
        _prefix = (prefix ?? string.Empty).Trim('/');
    }

    /// <summary>Libera o cliente <see cref="IAmazonS3" /> subjacente.</summary>
    public void Dispose() {
        _s3.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected override async Task<ObjectContent?> OpenObjectAsync(string key, CancellationToken cancellationToken) {
        try {
            var response = await _s3.GetObjectAsync(
                new GetObjectRequest { BucketName = _bucket, Key = ResolveKey(key) }, cancellationToken);
            return new ObjectContent(response.ResponseStream, response.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
            return null;
        }
    }

    /// <inheritdoc />
    protected override async Task UploadObjectAsync(string key, Stream stream, string contentType,
        long? contentLength, CancellationToken cancellationToken) {
        using var transfer = new TransferUtility(_s3);
        var request = new TransferUtilityUploadRequest {
            InputStream = stream,
            BucketName = _bucket,
            Key = ResolveKey(key),
            ContentType = contentType,
            AutoCloseStream = false
        };
        if (contentLength.HasValue) request.Headers.ContentLength = contentLength.Value;

        await transfer.UploadAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<BucketObject> ListObjectsAsync(string? prefix, long offsetTimestamp = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var fullPrefix = Combine(_prefix, prefix);
        var offset = offsetTimestamp != 0 ? DateTimeOffset.FromUnixTimeMilliseconds(offsetTimestamp) : (DateTimeOffset?)null;
        string? token = null;

        do {
            var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request {
                BucketName = _bucket, Prefix = fullPrefix, ContinuationToken = token, MaxKeys = 1000
            }, cancellationToken);

            token = response.IsTruncated == true ? response.NextContinuationToken : null;

            foreach (var obj in response.S3Objects ?? []) {
                cancellationToken.ThrowIfCancellationRequested();
                if (obj.Key.EndsWith('/')) continue;

                var modified = new DateTimeOffset(
                    DateTime.SpecifyKind(obj.LastModified ?? DateTime.UtcNow, DateTimeKind.Utc), TimeSpan.Zero);
                if (offset is not null && modified <= offset.Value) continue;

                yield return new BucketObject(_bucket, obj.Key, modified);
            }
        } while (token is not null);
    }

    /// <inheritdoc />
    public override Task DeleteObjectAsync(string key, CancellationToken cancellationToken = default) {
        return _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = ResolveKey(key) },
            cancellationToken);
    }

    /// <inheritdoc />
    public override async Task CopyObjectAsync(string sourceKey, string destinationKey,
        CancellationToken cancellationToken = default) {
        var src = ResolveKey(sourceKey);
        var dst = ResolveKey(destinationKey);
        if (string.Equals(src, dst, StringComparison.Ordinal)) return;

        await _s3.CopyObjectAsync(new CopyObjectRequest {
            SourceBucket = _bucket, SourceKey = src,
            DestinationBucket = _bucket, DestinationKey = dst,
            MetadataDirective = S3MetadataDirective.COPY
        }, cancellationToken);
    }

    /// <inheritdoc />
    public override Task<string> GetPreSignedUrlAsync(string key, TimeSpan ttl,
        FileAccess accessType = FileAccess.Read, CancellationToken cancellationToken = default) {
        var request = new GetPreSignedUrlRequest {
            BucketName = _bucket,
            Key = ResolveKey(key),
            Expires = DateTime.UtcNow.Add(ttl),
            Verb = accessType.HasFlag(FileAccess.Write) ? HttpVerb.PUT : HttpVerb.GET
        };
        return _s3.GetPreSignedURLAsync(request);
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

    private static IAmazonS3 CreateClient(S3StorageOptions o) {
        AWSCredentials? credentials = null;
        if (!string.IsNullOrWhiteSpace(o.AccessKey) && !string.IsNullOrWhiteSpace(o.SecretKey))
            credentials = new BasicAWSCredentials(o.AccessKey, o.SecretKey);

        if (!string.IsNullOrWhiteSpace(o.ServiceUrl)) {
            var cfg = new AmazonS3Config { ServiceURL = o.ServiceUrl, ForcePathStyle = o.ForcePathStyle };
            return credentials is not null ? new AmazonS3Client(credentials, cfg) : new AmazonS3Client(cfg);
        }

        var region = Environment.GetEnvironmentVariable("AWS_REGION")
                     ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
        if (region is not null) {
            var cfg = new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
            return credentials is not null ? new AmazonS3Client(credentials, cfg) : new AmazonS3Client(cfg);
        }

        return credentials is not null ? new AmazonS3Client(credentials) : new AmazonS3Client();
    }
}
