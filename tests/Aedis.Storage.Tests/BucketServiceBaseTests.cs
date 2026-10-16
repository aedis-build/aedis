using System.Runtime.CompilerServices;
using System.Text;
using Aedis.Storage.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Storage.Tests;

/// <summary>
///     Valida o Template Method <see cref="BucketServiceBase{T}" /> pelo contrato <see cref="IBucket{T}" />,
///     usando um provider fake em memória: a base deve aplicar o tratamento de stream/memória,
///     compor Move = Copy + Delete e reportar progresso — tudo herdado por qualquer provider real.
/// </summary>
public sealed class BucketServiceBaseTests
{
    private readonly IBucket<FakeBucket> _sut = new FakeBucket();

    private static Stream StreamOf(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static async Task<string> ReadAllAsync(Stream? stream) {
        stream.Should().NotBeNull();
        await using (stream!) {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
    }

    [Fact]
    public async Task Put_e_Get_fazem_roundtrip_pelo_contrato() {
        await _sut.PutObjectAsync("k.txt", StreamOf("dado"));

        (await ReadAllAsync(await _sut.GetObjectAsync("k.txt"))).Should().Be("dado");
    }

    [Fact]
    public async Task Get_inexistente_retorna_null() {
        (await _sut.GetObjectAsync("nope")).Should().BeNull();
    }

    [Fact]
    public async Task Get_aplica_o_tratamento_de_memoria_da_plataforma() {
        await _sut.PutObjectAsync("k.txt", StreamOf("pequeno"));

        var result = await _sut.GetObjectAsync("k.txt", StreamMode.Memory);

        result.Should().BeOfType<MemoryStream>();
    }

    [Fact]
    public async Task Move_compoe_copy_e_delete() {
        await _sut.PutObjectAsync("from.txt", StreamOf("dado"));

        await _sut.MoveObjectAsync("from.txt", "to.txt");

        (await _sut.GetObjectAsync("from.txt")).Should().BeNull();
        (await ReadAllAsync(await _sut.GetObjectAsync("to.txt"))).Should().Be("dado");
    }

    [Fact]
    public async Task Put_com_progresso_reporta_ate_100() {
        var onProgress = Substitute.For<Action<UploadProgress>>();

        await _sut.PutObjectAsync("k.txt", StreamOf("conteúdo de progresso"), onProgress: onProgress);

        onProgress.Received().Invoke(Arg.Is<UploadProgress>(p => p.PercentDone == 100));
    }

    /// <summary>Provider fake em memória — só implementa os passos específicos do Template Method.</summary>
    public sealed class FakeBucket : BucketServiceBase<FakeBucket>
    {
        private readonly Dictionary<string, byte[]> _store = new();

        protected override Task<ObjectContent?> OpenObjectAsync(string key, CancellationToken cancellationToken) {
            return Task.FromResult(_store.TryGetValue(key, out var bytes)
                ? new ObjectContent(new MemoryStream(bytes), bytes.Length)
                : (ObjectContent?)null);
        }

        protected override async Task UploadObjectAsync(string key, Stream stream, string contentType,
            long? contentLength, CancellationToken cancellationToken) {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            _store[key] = ms.ToArray();
        }

        public override async IAsyncEnumerable<BucketObject> ListObjectsAsync(string? prefix, long offsetTimestamp = 0,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            foreach (var key in _store.Keys) yield return new BucketObject("fake", key);
            await Task.CompletedTask;
        }

        public override Task DeleteObjectAsync(string key, CancellationToken cancellationToken = default) {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public override Task CopyObjectAsync(string sourceKey, string destinationKey,
            CancellationToken cancellationToken = default) {
            _store[destinationKey] = _store[sourceKey];
            return Task.CompletedTask;
        }

        public override Task<string> GetPreSignedUrlAsync(string key, TimeSpan ttl,
            FileAccess accessType = FileAccess.Read, CancellationToken cancellationToken = default) {
            return Task.FromResult($"https://fake/{key}?ttl={ttl.TotalSeconds}");
        }
    }

    /// <summary>Bucket cujo upload exercita todos os membros do stream com progresso (leitura, escrita, seek, dispose).</summary>
    public sealed class StreamProbingBucket : BucketServiceBase<StreamProbingBucket>
    {
        public long ObservedLength;
        public bool Seekable;

        protected override Task<ObjectContent?> OpenObjectAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult<ObjectContent?>(null);

        protected override async Task UploadObjectAsync(string key, Stream stream, string contentType,
            long? contentLength, CancellationToken cancellationToken) {
            Seekable = stream.CanSeek && stream.CanRead && stream.CanWrite;
            ObservedLength = stream.Length;
            var buffer = new byte[4];
            stream.Read(buffer, 0, 2).Should().Be(2);
            stream.ReadByte().Should().BeGreaterThan(-1);
            (await stream.ReadAsync(buffer, 0, 1, cancellationToken)).Should().Be(1);
            stream.Seek(0, SeekOrigin.End);
            stream.Position = stream.Length;
            stream.WriteByte(1);
            stream.Write(buffer, 0, 2);
            await stream.WriteAsync(buffer, 0, 1, cancellationToken);
            stream.Flush();
            await stream.FlushAsync(cancellationToken);
            stream.SetLength(stream.Length);
            stream.Position = 0;
            while (stream.ReadByte() != -1) { }
            stream.Dispose();
            await stream.DisposeAsync();
        }

        public override IAsyncEnumerable<BucketObject> ListObjectsAsync(string? prefix, long offsetTimestamp = 0,
            CancellationToken cancellationToken = default) => AsyncEnumerable.Empty<BucketObject>();

        public override Task DeleteObjectAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task CopyObjectAsync(string sourceKey, string destinationKey,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task<string> GetPreSignedUrlAsync(string key, TimeSpan ttl, FileAccess accessType = FileAccess.Read,
            CancellationToken cancellationToken = default) => Task.FromResult(key);
    }

    /// <summary>O stream de progresso delega todos os membros ao stream interno e reporta cada leitura/escrita.</summary>
    [Fact]
    public async Task Stream_de_progresso_delega_todos_os_membros_e_reporta_bytes() {
        var bucket = new StreamProbingBucket();
        var reports = new List<UploadProgress>();

        var source = new MemoryStream();
        source.Write("abcdef"u8);
        source.Position = 0;

        await bucket.PutObjectAsync("k", source, onProgress: reports.Add);

        bucket.Seekable.Should().BeTrue();
        bucket.ObservedLength.Should().Be(6);
        reports.Should().NotBeEmpty();
        reports.Select(r => r.TransferredBytes).Should().BeInAscendingOrder();
        reports.Should().AllSatisfy(r => r.TotalBytes.Should().Be(6));
    }
}
