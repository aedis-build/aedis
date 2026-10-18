using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Aedis.Storage.S3;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Storage.S3.Tests;

/// <summary>Construção por opções, upload via TransferUtility e listagem paginada com prefixo e corte por data.</summary>
public sealed class S3BucketServiceMoreTests
{
    private const string Bucket = "test-bucket";
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    public sealed class TestBucket(IAmazonS3 s3, string prefix = "") : S3BucketService<TestBucket>(s3, Bucket, prefix);

    /// <summary>Bucket construído a partir das opções (cliente real do SDK, sem chamadas de rede).</summary>
    public sealed class OptionsBucket(S3StorageOptions options) : S3BucketService<OptionsBucket>(options);

    [Fact]
    public void Construtor_por_opcoes_valida_e_cria_o_cliente_com_endpoint_e_credenciais() {
        var act = () => new OptionsBucket(new S3StorageOptions { BucketName = " " });
        act.Should().Throw<ArgumentException>().WithParameterName("options");

        using var withKeys = new OptionsBucket(new S3StorageOptions {
            BucketName = Bucket, Prefix = "/p/", AccessKey = "ak", SecretKey = "sk",
            ServiceUrl = "http://localhost:4566", ForcePathStyle = true
        });
        using var anonymous = new OptionsBucket(new S3StorageOptions { BucketName = Bucket, ServiceUrl = "http://localhost:4566" });

        withKeys.Should().NotBeNull();
        anonymous.Should().NotBeNull();
    }

    [Fact]
    public async Task Put_envia_via_TransferUtility_com_content_type_e_prefixo() {
        var sut = new TestBucket(_s3, "uploads");
        _s3.Config.Returns(new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.USEast1 });
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });

        await sut.PutObjectAsync("file.txt", new MemoryStream(Encoding.UTF8.GetBytes("abc")), "text/plain", contentLength: 3);

        await _s3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.BucketName == Bucket && r.Key == "uploads/file.txt" && r.ContentType == "text/plain"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_combina_prefixo_filtra_por_timestamp_e_pagina() {
        var sut = new TestBucket(_s3, "data");
        var old = DateTime.UtcNow.AddDays(-2);
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(
                new ListObjectsV2Response {
                    IsTruncated = true, NextContinuationToken = "t",
                    S3Objects = [new S3Object { Key = "data/2024/a.txt", LastModified = DateTime.UtcNow }]
                },
                new ListObjectsV2Response {
                    IsTruncated = false,
                    S3Objects = [new S3Object { Key = "data/2024/old.txt", LastModified = old }]
                });

        var keys = new List<string>();
        await foreach (var obj in sut.ListObjectsAsync("2024/", DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds()))
            keys.Add(obj.FilePath);

        keys.Should().Equal("data/2024/a.txt");
        await _s3.Received(2).ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.Prefix == "data/2024"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Guardas_do_construtor_e_listagem_sem_prefixo_sem_objetos_e_sem_data() {
        FluentActions.Invoking(() => new TestBucket(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new OptionsBucket(new S3StorageOptions { BucketName = Bucket, ServiceUrl = "http://localhost:4566" }))
            .Should().NotThrow();
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { IsTruncated = false, S3Objects = null },
                new ListObjectsV2Response { IsTruncated = false, S3Objects = [new S3Object { Key = "x.txt" }, new S3Object { Key = "pasta/" }] });

        var vazio = new List<Aedis.Storage.Abstractions.BucketObject>();
        await foreach (var o in new TestBucket(_s3).ListObjectsAsync(null)) vazio.Add(o);
        var semData = new List<Aedis.Storage.Abstractions.BucketObject>();
        await foreach (var o in new TestBucket(_s3, "p").ListObjectsAsync(null)) semData.Add(o);

        vazio.Should().BeEmpty();
        semData.Should().ContainSingle().Which.LastModified.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        await _s3.Received(1).ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.Prefix == null), Arg.Any<CancellationToken>());
        await _s3.Received(1).ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.Prefix == "p"), Arg.Any<CancellationToken>());
    }
}
