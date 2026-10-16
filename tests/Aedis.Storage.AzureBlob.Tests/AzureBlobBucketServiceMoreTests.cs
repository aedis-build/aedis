using System.Text;
using Aedis.Storage.AzureBlob;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Storage.AzureBlob.Tests;

/// <summary>Construção por opções, upload, listagem com prefixo e corte por data, e URLs SAS por tipo de acesso.</summary>
public sealed class AzureBlobBucketServiceMoreTests
{
    private readonly BlobContainerClient _container = Substitute.For<BlobContainerClient>();
    private readonly BlobClient _blob = Substitute.For<BlobClient>();

    public AzureBlobBucketServiceMoreTests() {
        _container.GetBlobClient(Arg.Any<string>()).Returns(_blob);
        _container.Name.Returns("c");
    }

    public sealed class TestContainer(BlobContainerClient container, string prefix = "")
        : AzureBlobBucketService<TestContainer>(container, prefix);

    /// <summary>Container construído a partir das opções (cliente real do SDK, sem chamadas de rede).</summary>
    public sealed class OptionsContainer(AzureBlobStorageOptions options) : AzureBlobBucketService<OptionsContainer>(options);

    private TestContainer Sut(string prefix = "") => new(_container, prefix);

    [Fact]
    public void Construtor_por_opcoes_valida_connection_string_e_container() {
        var semConexao = () => new OptionsContainer(new AzureBlobStorageOptions { ConnectionString = " ", ContainerName = "c" });
        var semContainer = () => new OptionsContainer(new AzureBlobStorageOptions { ConnectionString = "UseDevelopmentStorage=true", ContainerName = "" });

        semConexao.Should().Throw<ArgumentException>();
        semContainer.Should().Throw<ArgumentException>();
        new OptionsContainer(new AzureBlobStorageOptions { ConnectionString = "UseDevelopmentStorage=true", ContainerName = "c", Prefix = "/p/" })
            .Should().NotBeNull();
    }

    [Fact]
    public async Task Put_envia_com_content_type_e_prefixo() {
        await Sut("uploads").PutObjectAsync("file.txt", new MemoryStream(Encoding.UTF8.GetBytes("abc")), "text/plain");

        _container.Received(1).GetBlobClient("uploads/file.txt");
        await _blob.Received(1).UploadAsync(Arg.Any<Stream>(),
            Arg.Is<BlobUploadOptions>(o => o.HttpHeaders.ContentType == "text/plain"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_combina_prefixo_ignora_pastas_e_filtra_por_timestamp() {
        var old = DateTimeOffset.UtcNow.AddDays(-2);
        var items = new[] {
            BlobsModelFactory.BlobItem(name: "data/2024/a.txt",
                properties: BlobsModelFactory.BlobItemProperties(false, lastModified: DateTimeOffset.UtcNow)),
            BlobsModelFactory.BlobItem(name: "data/2024/folder/",
                properties: BlobsModelFactory.BlobItemProperties(false, lastModified: DateTimeOffset.UtcNow)),
            BlobsModelFactory.BlobItem(name: "data/2024/old.txt",
                properties: BlobsModelFactory.BlobItemProperties(false, lastModified: old))
        };
        _container.GetBlobsAsync(Arg.Any<BlobTraits>(), Arg.Any<BlobStates>(), "data/2024", Arg.Any<CancellationToken>())
            .Returns(AsyncPageable<BlobItem>.FromPages([Page<BlobItem>.FromValues(items, null, Substitute.For<Response>())]));

        var keys = new List<string>();
        await foreach (var obj in Sut("data").ListObjectsAsync("2024/", DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds()))
            keys.Add(obj.FilePath);

        keys.Should().Equal("data/2024/a.txt");
    }

    [Theory]
    [InlineData(FileAccess.Read)]
    [InlineData(FileAccess.Write)]
    [InlineData(FileAccess.ReadWrite)]
    public async Task PreSignedUrl_gera_SAS_com_as_permissoes_do_acesso(FileAccess access) {
        _blob.GenerateSasUri(Arg.Any<BlobSasBuilder>()).Returns(new Uri("https://acct.blob.core.windows.net/c/file.txt?sig=x"));

        var url = await Sut().GetPreSignedUrlAsync("file.txt", TimeSpan.FromMinutes(5), access);

        url.Should().Contain("sig=x");
        _blob.Received(1).GenerateSasUri(Arg.Is<BlobSasBuilder>(b => b.BlobName == "file.txt" && b.BlobContainerName == "c"));
    }

    [Fact]
    public async Task PreSignedUrl_rejeita_acesso_invalido() {
        var act = () => Sut().GetPreSignedUrlAsync("file.txt", TimeSpan.FromMinutes(5), (FileAccess)99);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
