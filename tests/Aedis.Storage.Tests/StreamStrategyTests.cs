using System.Text;
using Aedis.Storage.Abstractions;
using Aedis.Storage.Abstractions.Streaming;
using Xunit;

namespace Aedis.Storage.Tests;

/// <summary>
///     O diferencial da plataforma: seleção correta da estratégia de stream/memória conforme
///     o <see cref="StreamMode" /> e o tamanho do conteúdo.
/// </summary>
public sealed class StreamStrategyTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("conteúdo de teste de stream");

    private static StreamContext Context(StreamMode mode, long contentLength) => new() {
        Mode = mode,
        SourceStream = new MemoryStream(Payload),
        ContentLength = contentLength
    };

    private static async Task<byte[]> ToBytesAsync(Stream stream) {
        await using (stream) {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }
    }

    [Fact]
    public async Task Memory_pequeno_resulta_em_MemoryStream() {
        var ctx = Context(StreamMode.Memory, Payload.Length);

        await StreamStrategyResolver.CreateDefault().ExecuteAsync(ctx);

        Assert.IsType<MemoryStream>(ctx.Result);
        Assert.Equal(Payload, await ToBytesAsync(ctx.Result!));
    }

    [Fact]
    public async Task Memory_acima_do_threshold_faz_spool_para_arquivo() {
        // ContentLength forçado acima do limite — a estratégia de TempFile deve assumir.
        var ctx = Context(StreamMode.Memory, MemoryStreamStrategy.MemoryThreshold + 1);

        await StreamStrategyResolver.CreateDefault().ExecuteAsync(ctx);

        Assert.IsType<FileStream>(ctx.Result);
        Assert.Equal(Payload, await ToBytesAsync(ctx.Result!));
    }

    [Fact]
    public async Task TempFile_resulta_em_FileStream() {
        var ctx = Context(StreamMode.TempFile, Payload.Length);

        await StreamStrategyResolver.CreateDefault().ExecuteAsync(ctx);

        Assert.IsType<FileStream>(ctx.Result);
        Assert.Equal(Payload, await ToBytesAsync(ctx.Result!));
    }

    [Theory]
    [InlineData(StreamMode.Chunked)]
    [InlineData(StreamMode.Default)]
    public async Task Chunked_e_Default_passam_o_stream_de_origem_sem_copiar(StreamMode mode) {
        var ctx = Context(mode, Payload.Length);
        var source = ctx.SourceStream;

        await StreamStrategyResolver.CreateDefault().ExecuteAsync(ctx);

        Assert.Same(source, ctx.Result);
    }
}
