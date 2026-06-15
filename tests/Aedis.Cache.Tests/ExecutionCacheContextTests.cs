using Aedis.Cache;
using Aedis.Cache.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Aedis.Cache.Tests;

/// <summary>
///     <see cref="ExecutionCacheContext" /> e sua factory sobre um <see cref="ICache" /> substituído:
///     última execução, deduplicação de itens e commit do marcador.
/// </summary>
public sealed class ExecutionCacheContextTests
{
    private readonly ICache _cache = Substitute.For<ICache>();

    private ExecutionCacheContext CreateContext() => new(_cache, NullLogger<ExecutionCacheContext>.Instance);

    [Fact]
    public async Task GetLastExecution_sem_valor_devolve_null() {
        _cache.GetStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        (await CreateContext().GetLastExecution()).Should().BeNull();
    }

    [Fact]
    public async Task GetLastExecution_converte_os_ticks() {
        var instant = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        _cache.GetStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(instant.Ticks.ToString());

        var result = await CreateContext().GetLastExecution();

        result.Should().Be(instant);
    }

    [Fact]
    public async Task GetLastExecution_valor_invalido_devolve_null() {
        _cache.GetStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("não-é-número");

        (await CreateContext().GetLastExecution()).Should().BeNull();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_propaga_o_resultado_do_dedup() {
        _cache.SetIfNotExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>()).Returns(true, false);

        var first = await CreateContext().MarkAsProcessedAsync("item-1");
        var second = await CreateContext().MarkAsProcessedAsync("item-1");

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task CommitAsync_persiste_o_marcador_de_ultima_execucao() {
        await CreateContext().CommitAsync();

        await _cache.Received(1).SetStringAsync(
            Arg.Is<string>(k => k.Contains("last-execution")),
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_sem_commit_nao_lanca() {
        var context = CreateContext();

        var act = async () => await context.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_apos_commit_nao_lanca() {
        var context = CreateContext();
        await context.CommitAsync();

        var act = async () => await context.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Factory_cria_contextos_independentes() {
        var factory = new ExecutionCacheContextFactory(_cache, NullLogger<ExecutionCacheContext>.Instance);

        var a = factory.Create();
        var b = factory.Create();

        a.Should().NotBeNull();
        b.Should().NotBeSameAs(a, "cada execução recebe um contexto novo com seu próprio estado de commit");
    }
}
