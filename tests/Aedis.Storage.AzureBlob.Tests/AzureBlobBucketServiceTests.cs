using System.Text;
using Aedis.Storage.Abstractions;
using Aedis.Storage.AzureBlob;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Aedis.Storage.AzureBlob.Tests;

/// <summary>
///     Mapeamento do provider Azure Blob ↔ <see cref="BlobContainerClient" />/<see cref="BlobClient" />
///     (substituídos com NSubstitute) e a integração com o Template Method. Sem tocar o Azure real.
/// </summary>
public sealed class AzureBlobBucketServiceTests
{
    private readonly BlobContainerClient _container = Substitute.For<BlobContainerClient>();
    private readonly BlobClient _blob = Substitute.For<BlobClient>();

    public AzureBlobBucketServiceTests() {
        _container.GetBlobClient(Arg.Any<string>()).Returns(_blob);
    }

    /// <summary>Marcador de container de teste, injetando o BlobContainerClient substituído.</summary>
    public sealed class TestContainer(BlobContainerClient container, string prefix = "")
        : AzureBlobBucketService<TestContainer>(container, prefix);

    private IBucket<TestContainer> Sut(string prefix = "") => new TestContainer(_container, prefix);

    [Fact]
    public async Task Get_baixa_e_aplica_o_tratamento_de_stream() {
        var bytes = Encoding.UTF8.GetBytes("conteúdo azure");
        var streaming = BlobsModelFactory.BlobDownloadStreamingResult(
            content: new MemoryStream(bytes),
            details: BlobsModelFactory.BlobDownloadDetails(contentLength: bytes.Length));
        _blob.DownloadStreamingAsync(Arg.Any<BlobDownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(streaming, Substitute.For<Response>()));

        var result = await Sut().GetObjectAsync("file.txt", StreamMode.Memory);

        result.Should().BeOfType<MemoryStream>();
        _container.Received().GetBlobClient("file.txt");
    }

    [Fact]
    public async Task Get_retorna_null_quando_blob_responde_404() {
        _blob.DownloadStreamingAsync(Arg.Any<BlobDownloadOptions>(), Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(404, "not found"));

        (await Sut().GetObjectAsync("missing.txt")).Should().BeNull();
    }

    [Fact]
    public async Task Delete_resolve_a_chave_e_chama_o_blob() {
        await Sut().DeleteObjectAsync("file.txt");

        _container.Received(1).GetBlobClient("file.txt");
        await _blob.Received(1).DeleteIfExistsAsync(
            Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Copy_chama_StartCopyFromUri_no_destino() {
        await Sut().CopyObjectAsync("a.txt", "b.txt");

        _container.Received().GetBlobClient("a.txt");
        _container.Received().GetBlobClient("b.txt");
        await _blob.Received(1).StartCopyFromUriAsync(
            Arg.Any<Uri>(), Arg.Any<BlobCopyFromUriOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Copy_mesma_chave_nao_chama_o_SDK() {
        await Sut().CopyObjectAsync("same.txt", "same.txt");

        await _blob.DidNotReceive().StartCopyFromUriAsync(
            Arg.Any<Uri>(), Arg.Any<BlobCopyFromUriOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prefixo_e_aplicado_as_chaves() {
        await Sut("uploads").DeleteObjectAsync("file.txt");

        _container.Received(1).GetBlobClient("uploads/file.txt");
    }
}
