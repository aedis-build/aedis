using System.Net;
using System.Text;
using Aedis.Storage.Abstractions;
using Aedis.Storage.S3;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Aedis.Storage.S3.Tests;

/// <summary>
///     Mapeamento do provider S3 ↔ <see cref="IAmazonS3" /> (substituído com NSubstitute) e a integração
///     com o Template Method (Get passa pelo tratamento de stream da plataforma). Sem tocar a AWS real.
/// </summary>
public sealed class S3BucketServiceTests
{
    private const string Bucket = "test-bucket";
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();
    private readonly IBucket<TestBucket> _sut;

    public S3BucketServiceTests() {
        _sut = new TestBucket(_s3);
    }

    /// <summary>Marcador de bucket de teste, injetando o IAmazonS3 substituído.</summary>
    public sealed class TestBucket(IAmazonS3 s3, string prefix = "") : S3BucketService<TestBucket>(s3, Bucket, prefix);

    [Fact]
    public async Task Get_baixa_e_aplica_o_tratamento_de_stream() {
        var bytes = Encoding.UTF8.GetBytes("conteúdo s3");
        _s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectResponse { ResponseStream = new MemoryStream(bytes), ContentLength = bytes.Length });

        var result = await _sut.GetObjectAsync("file.txt", StreamMode.Memory);

        result.Should().BeOfType<MemoryStream>();
        await _s3.Received(1).GetObjectAsync(
            Arg.Is<GetObjectRequest>(r => r.BucketName == Bucket && r.Key == "file.txt"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_retorna_null_quando_S3_responde_NotFound() {
        _s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });

        (await _sut.GetObjectAsync("missing.txt")).Should().BeNull();
    }

    [Fact]
    public async Task List_mapeia_S3Objects_para_BucketObject() {
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response {
                IsTruncated = false,
                S3Objects = [
                    new S3Object { BucketName = Bucket, Key = "a.txt", LastModified = DateTime.UtcNow },
                    new S3Object { BucketName = Bucket, Key = "folder/", LastModified = DateTime.UtcNow }
                ]
            });

        var keys = new List<string>();
        await foreach (var obj in _sut.ListObjectsAsync(null)) keys.Add(obj.FilePath);

        keys.Should().ContainSingle().Which.Should().Be("a.txt"); // "folder/" (pseudo-dir) é ignorado
    }

    [Fact]
    public async Task Delete_chama_o_SDK_com_bucket_e_key() {
        await _sut.DeleteObjectAsync("file.txt");

        await _s3.Received(1).DeleteObjectAsync(
            Arg.Is<DeleteObjectRequest>(r => r.BucketName == Bucket && r.Key == "file.txt"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Copy_mesma_chave_nao_chama_o_SDK() {
        await _sut.CopyObjectAsync("same.txt", "same.txt");

        await _s3.DidNotReceive().CopyObjectAsync(Arg.Any<CopyObjectRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Copy_chama_o_SDK_com_origem_e_destino() {
        await _sut.CopyObjectAsync("a.txt", "b.txt");

        await _s3.Received(1).CopyObjectAsync(
            Arg.Is<CopyObjectRequest>(r => r.SourceKey == "a.txt" && r.DestinationKey == "b.txt"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(FileAccess.Read, HttpVerb.GET)]
    [InlineData(FileAccess.Write, HttpVerb.PUT)]
    public async Task PreSignedUrl_usa_o_verbo_conforme_o_acesso(FileAccess access, HttpVerb expectedVerb) {
        _s3.GetPreSignedURLAsync(Arg.Any<GetPreSignedUrlRequest>()).Returns("https://signed");

        var url = await _sut.GetPreSignedUrlAsync("file.txt", TimeSpan.FromMinutes(5), access);

        url.Should().Be("https://signed");
        await _s3.Received(1).GetPreSignedURLAsync(
            Arg.Is<GetPreSignedUrlRequest>(r => r.Key == "file.txt" && r.Verb == expectedVerb));
    }

    [Fact]
    public async Task Prefixo_e_aplicado_as_chaves() {
        var sut = new TestBucket(_s3, "uploads");

        await sut.DeleteObjectAsync("file.txt");

        await _s3.Received(1).DeleteObjectAsync(
            Arg.Is<DeleteObjectRequest>(r => r.Key == "uploads/file.txt"),
            Arg.Any<CancellationToken>());
    }
}
