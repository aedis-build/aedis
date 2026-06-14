using System.Text;
using Aedis.Storage.Abstractions;
using Xunit;

namespace Aedis.Storage.Tests;

/// <summary>
///     Comportamento real do <see cref="DirectoryService" /> (impl FileSystem default):
///     roundtrip, listagem, copy/move/delete, modos de stream e bloqueio de path traversal.
/// </summary>
public sealed class DirectoryServiceTests : IDisposable
{
    private readonly string _baseDir;
    private readonly DirectoryService _sut;

    public DirectoryServiceTests() {
        _baseDir = Path.Combine(Path.GetTempPath(), "aedis-storage-tests", Guid.NewGuid().ToString("N"));
        _sut = new DirectoryService(_baseDir);
    }

    public void Dispose() {
        try {
            if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true);
        }
        catch {
            // best-effort cleanup
        }
    }

    private static Stream StreamOf(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static async Task<string> ReadAllAsync(Stream stream) {
        await using (stream) {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
    }

    [Fact]
    public void Construtor_cria_a_pasta_base_se_nao_existir() {
        Assert.True(Directory.Exists(_baseDir));
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_baseDir)), _sut.BasePath);
    }

    [Fact]
    public async Task Put_e_Get_fazem_roundtrip_do_conteudo() {
        await _sut.PutObjectAsync("docs/hello.txt", StreamOf("olá mundo"));

        var result = await _sut.GetObjectAsync("docs/hello.txt");

        Assert.NotNull(result);
        Assert.Equal("olá mundo", await ReadAllAsync(result!));
    }

    [Fact]
    public async Task Get_retorna_null_quando_objeto_nao_existe() {
        Assert.Null(await _sut.GetObjectAsync("inexistente.txt"));
    }

    [Fact]
    public async Task Get_no_modo_Memory_retorna_MemoryStream() {
        await _sut.PutObjectAsync("a.txt", StreamOf("pequeno"));

        var result = await _sut.GetObjectAsync("a.txt", StreamMode.Memory);

        Assert.IsType<MemoryStream>(result);
        Assert.Equal("pequeno", await ReadAllAsync(result!));
    }

    [Theory]
    [InlineData(StreamMode.Default)]
    [InlineData(StreamMode.Chunked)]
    [InlineData(StreamMode.TempFile)]
    [InlineData(StreamMode.Memory)]
    public async Task Get_preserva_conteudo_em_todos_os_modos(StreamMode mode) {
        await _sut.PutObjectAsync("x.bin", StreamOf("conteúdo-" + mode));

        var result = await _sut.GetObjectAsync("x.bin", mode);

        Assert.NotNull(result);
        Assert.Equal("conteúdo-" + mode, await ReadAllAsync(result!));
    }

    [Fact]
    public async Task List_retorna_os_objetos_enviados() {
        await _sut.PutObjectAsync("f1.txt", StreamOf("1"));
        await _sut.PutObjectAsync("sub/f2.txt", StreamOf("2"));

        var keys = new List<string>();
        await foreach (var obj in _sut.ListObjectsAsync(null)) keys.Add(obj.FilePath);

        Assert.Contains("f1.txt", keys);
        Assert.Contains("sub/f2.txt", keys);
    }

    [Fact]
    public async Task Delete_remove_o_objeto() {
        await _sut.PutObjectAsync("del.txt", StreamOf("x"));
        await _sut.DeleteObjectAsync("del.txt");

        Assert.Null(await _sut.GetObjectAsync("del.txt"));
    }

    [Fact]
    public async Task Copy_duplica_e_mantem_a_origem() {
        await _sut.PutObjectAsync("src.txt", StreamOf("dado"));

        await _sut.CopyObjectAsync("src.txt", "dst.txt");

        Assert.Equal("dado", await ReadAllAsync((await _sut.GetObjectAsync("src.txt"))!));
        Assert.Equal("dado", await ReadAllAsync((await _sut.GetObjectAsync("dst.txt"))!));
    }

    [Fact]
    public async Task Move_relocaliza_e_remove_a_origem() {
        await _sut.PutObjectAsync("from.txt", StreamOf("dado"));

        await _sut.MoveObjectAsync("from.txt", "to.txt");

        Assert.Null(await _sut.GetObjectAsync("from.txt"));
        Assert.Equal("dado", await ReadAllAsync((await _sut.GetObjectAsync("to.txt"))!));
    }

    [Fact]
    public async Task Put_reporta_progresso_ate_100() {
        var reports = new List<int>();

        await _sut.PutObjectAsync("p.txt", StreamOf("conteúdo de progresso"),
            onProgress: p => reports.Add(p.PercentDone));

        Assert.NotEmpty(reports);
        Assert.Equal(100, reports[^1]);
    }

    [Theory]
    [InlineData("../escapou.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("sub/../../fora.txt")]
    public async Task Bloqueia_path_traversal(string maliciousKey) {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.PutObjectAsync(maliciousKey, StreamOf("x")));
    }
}
