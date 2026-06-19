using System.Text;
using Aedis.Domain.Strategy.Abstractions;
using Aedis.Storage.Abstractions;
using Aedis.Storage.Abstractions.Streaming;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Storage.Tests;

/// <summary>
///     O diferencial da plataforma: seleção correta da estratégia de stream/memória conforme
///     o <see cref="StreamMode" /> e o tamanho do conteúdo. Exercita as estratégias e o resolver reais.
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

        ctx.Result.Should().BeOfType<MemoryStream>();
        (await ToBytesAsync(ctx.Result!)).Should().Equal(Payload);
    }

    [Fact]
    public async Task Memory_acima_do_threshold_faz_spool_para_arquivo() {
        var ctx = Context(StreamMode.Memory, MemoryStreamStrategy.MemoryThreshold + 1);

        await StreamStrategyResolver.CreateDefault().ExecuteAsync(ctx);

        ctx.Result.Should().BeOfType<FileStream>();
        (await ToBytesAsync(ctx.Result!)).Should().Equal(Payload);
    }

    [Fact]
    public async Task TempFile_resulta_em_FileStream() {
        var ctx = Context(StreamMode.TempFile, Payload.Length);

        await StreamStrategyResolver.CreateDefault().ExecuteAsync(ctx);

        ctx.Result.Should().BeOfType<FileStream>();
        (await ToBytesAsync(ctx.Result!)).Should().Equal(Payload);
    }

    [Theory]
    [InlineData(StreamMode.Chunked)]
    [InlineData(StreamMode.Default)]
    public async Task Chunked_e_Default_passam_o_stream_de_origem_sem_copiar(StreamMode mode) {
        var ctx = Context(mode, Payload.Length);
        var source = ctx.SourceStream;

        await StreamStrategyResolver.CreateDefault().ExecuteAsync(ctx);

        ctx.Result.Should().BeSameAs(source);
    }

    [Fact]
    public async Task Resolver_executa_apenas_a_estrategia_que_aceita_o_contexto() {
        var aceita = Substitute.For<IStrategy<StreamContext>>();
        aceita.CanHandle(Arg.Any<StreamContext>()).Returns(true);

        var ignora = Substitute.For<IStrategy<StreamContext>>();
        ignora.CanHandle(Arg.Any<StreamContext>()).Returns(false);

        var resolver = new StreamStrategyResolver([ignora, aceita]);
        var ctx = Context(StreamMode.Default, Payload.Length);

        await resolver.ExecuteAsync(ctx);

        await aceita.Received(1).ExecuteAsync(ctx, Arg.Any<CancellationToken>());
        await ignora.DidNotReceive().ExecuteAsync(Arg.Any<StreamContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolver_lanca_quando_nenhuma_estrategia_aceita() {
        var nenhuma = Substitute.For<IStrategy<StreamContext>>();
        nenhuma.CanHandle(Arg.Any<StreamContext>()).Returns(false);

        var resolver = new StreamStrategyResolver([nenhuma]);
        var act = () => resolver.ExecuteAsync(Context(StreamMode.Memory, 0));

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
