using System.Text;
using Aedis.Storage.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Storage.Tests;

/// <summary>
///     Comportamento real do <see cref="DirectoryService" /> (impl FileSystem default), exercitado
///     pelo contrato <see cref="IDirectory" />: roundtrip, listagem, copy/move/delete, modos de stream,
///     progresso e bloqueio de path traversal.
/// </summary>
public sealed class DirectoryServiceTests : IDisposable
{
    private readonly string _baseDir;
    private readonly IDirectory _sut;

    public DirectoryServiceTests() {
        _baseDir = Path.Combine(Path.GetTempPath(), "aedis-storage-tests", Guid.NewGuid().ToString("N"));
        _sut = new DirectoryService(_baseDir);
    }

    public void Dispose() {
        try {
            if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true);
        }
        catch {
        }
    }

    private static Stream StreamOf(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static async Task<string> ReadAllAsync(Stream? stream) {
        stream.Should().NotBeNull();
        await using (stream!) {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
    }

    [Fact]
    public void Construtor_cria_a_pasta_base_e_expoe_o_BasePath() {
        var svc = new DirectoryService(_baseDir);

        Directory.Exists(_baseDir).Should().BeTrue();
        svc.BasePath.Should().Be(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_baseDir)));
    }

    [Fact]
    public void Construtor_exige_basePath() {
        var act = () => new DirectoryService("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Put_e_Get_fazem_roundtrip_do_conteudo() {
        await _sut.PutObjectAsync("docs/hello.txt", StreamOf("olá mundo"));

        var result = await _sut.GetObjectAsync("docs/hello.txt");

        (await ReadAllAsync(result)).Should().Be("olá mundo");
    }

    [Fact]
    public async Task Get_retorna_null_quando_objeto_nao_existe() {
        (await _sut.GetObjectAsync("inexistente.txt")).Should().BeNull();
    }

    [Fact]
    public async Task Get_no_modo_Memory_retorna_MemoryStream() {
        await _sut.PutObjectAsync("a.txt", StreamOf("pequeno"));

        var result = await _sut.GetObjectAsync("a.txt", StreamMode.Memory);

        result.Should().BeOfType<MemoryStream>();
    }

    [Theory]
    [InlineData(StreamMode.Default)]
    [InlineData(StreamMode.Chunked)]
    [InlineData(StreamMode.TempFile)]
    [InlineData(StreamMode.Memory)]
    public async Task Get_preserva_conteudo_em_todos_os_modos(StreamMode mode) {
        await _sut.PutObjectAsync("x.bin", StreamOf("conteúdo-" + mode));

        var result = await _sut.GetObjectAsync("x.bin", mode);

        (await ReadAllAsync(result)).Should().Be("conteúdo-" + mode);
    }

    [Fact]
    public async Task List_retorna_os_objetos_enviados() {
        await _sut.PutObjectAsync("f1.txt", StreamOf("1"));
        await _sut.PutObjectAsync("sub/f2.txt", StreamOf("2"));

        var keys = new List<string>();
        await foreach (var obj in _sut.ListObjectsAsync(null)) keys.Add(obj.FilePath);

        keys.Should().Contain("f1.txt").And.Contain("sub/f2.txt");
    }

    [Fact]
    public async Task Delete_remove_o_objeto() {
        await _sut.PutObjectAsync("del.txt", StreamOf("x"));

        await _sut.DeleteObjectAsync("del.txt");

        (await _sut.GetObjectAsync("del.txt")).Should().BeNull();
    }

    [Fact]
    public async Task Copy_duplica_e_mantem_a_origem() {
        await _sut.PutObjectAsync("src.txt", StreamOf("dado"));

        await _sut.CopyObjectAsync("src.txt", "dst.txt");

        (await ReadAllAsync(await _sut.GetObjectAsync("src.txt"))).Should().Be("dado");
        (await ReadAllAsync(await _sut.GetObjectAsync("dst.txt"))).Should().Be("dado");
    }

    [Fact]
    public async Task Move_relocaliza_e_remove_a_origem() {
        await _sut.PutObjectAsync("from.txt", StreamOf("dado"));

        await _sut.MoveObjectAsync("from.txt", "to.txt");

        (await _sut.GetObjectAsync("from.txt")).Should().BeNull();
        (await ReadAllAsync(await _sut.GetObjectAsync("to.txt"))).Should().Be("dado");
    }

    [Fact]
    public async Task Put_reporta_progresso_ate_100() {
        var onProgress = Substitute.For<Action<UploadProgress>>();

        await _sut.PutObjectAsync("p.txt", StreamOf("conteúdo de progresso"), onProgress: onProgress);

        onProgress.Received().Invoke(Arg.Is<UploadProgress>(p => p.PercentDone == 100));
    }

    [Theory]
    [InlineData("../escapou.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("sub/../../fora.txt")]
    public async Task Bloqueia_path_traversal(string maliciousKey) {
        var act = () => _sut.PutObjectAsync(maliciousKey, StreamOf("x"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
