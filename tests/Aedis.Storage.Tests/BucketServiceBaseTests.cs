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
}
