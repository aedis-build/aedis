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

    [Fact]
    public async Task List_com_prefixo_inexistente_e_vazia_e_com_prefixo_e_corte_filtra_por_data() {
        var nada = new List<BucketObject>();
        await foreach (var o in _sut.ListObjectsAsync("inexistente")) nada.Add(o);
        nada.Should().BeEmpty();

        await _sut.PutObjectAsync("sub/a.txt", StreamOf("a"), "text/plain");
        await _sut.PutObjectAsync("sub/b.txt", StreamOf("b"), "text/plain");
        File.SetLastWriteTimeUtc(Path.Combine(_baseDir, "sub", "a.txt"), DateTime.UtcNow.AddDays(-2));

        var recentes = new List<BucketObject>();
        await foreach (var o in _sut.ListObjectsAsync("sub", DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds())) recentes.Add(o);
        recentes.Select(o => o.FilePath).Should().Equal("sub/b.txt");
    }

    [Fact]
    public async Task Progresso_sem_tamanho_conhecido_reporta_zero_por_cento() {
        var percents = new List<int>();

        await _sut.PutObjectAsync("p.txt", new NonSeekableStream(StreamOf("conteudo")), "text/plain", onProgress: p => percents.Add(p.PercentDone));

        percents.Should().NotBeEmpty().And.AllSatisfy(p => p.Should().Be(0));
    }

    [Fact]
    public async Task Copy_para_a_mesma_chave_nao_faz_nada_e_origem_ausente_falha() {
        await _sut.PutObjectAsync("c.txt", StreamOf("c"), "text/plain");

        await _sut.CopyObjectAsync("c.txt", "c.txt");
        (await ReadAllAsync(await _sut.GetObjectAsync("c.txt"))).Should().Be("c");
        await FluentActions.Awaiting(() => _sut.CopyObjectAsync("nao-existe.txt", "d.txt")).Should().ThrowAsync<FileNotFoundException>();
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
