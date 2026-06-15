using Aedis.Cache;
using Aedis.Cache.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Aedis.Cache.Tests;

/// <summary>
///     <see cref="BatchCacheService" /> sobre um <see cref="ICache" /> substituído: retomada por
///     checkpoint, deduplicação real de itens e contabilização de progresso — incluindo a garantia de
///     que progress não é no-op e que MarkProcessed deduplica (corrigindo os defeitos da origem).
/// </summary>
public sealed class BatchCacheServiceTests
{
    private readonly ICache _cache = Substitute.For<ICache>();
    private readonly BatchCacheService _sut;

    public BatchCacheServiceTests() {
        _sut = new BatchCacheService(_cache, NullLogger<BatchCacheService>.Instance);
    }

    [Fact]
    public async Task GetCheckpointAsync_sem_lideranca_devolve_null() {
        _cache.IsLeaderAsync("batch-1", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((IAsyncDisposable?)null);

        var result = await _sut.GetCheckpointAsync("batch-1", TimeSpan.FromMinutes(1));

        result.Should().BeNull("sem liderança não há checkpoint");
    }

    [Fact]
    public async Task GetCheckpointAsync_com_lideranca_retoma_a_linha_salva() {
        var lockHandle = Substitute.For<IAsyncDisposable>();
        _cache.IsLeaderAsync("batch-1", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(lockHandle);
        _cache.GetStringAsync(Arg.Is<string>(k => k.Contains("checkpoint")), Arg.Any<CancellationToken>())
            .Returns("42");

        var result = await _sut.GetCheckpointAsync("batch-1", TimeSpan.FromMinutes(1));

        result.Should().NotBeNull();
        result!.Checkpoint.Should().Be(42);
    }

    [Fact]
    public async Task GetCheckpointAsync_sem_valor_comeca_do_zero() {
        var lockHandle = Substitute.For<IAsyncDisposable>();
        _cache.IsLeaderAsync("batch-1", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(lockHandle);
        _cache.GetStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await _sut.GetCheckpointAsync("batch-1", TimeSpan.FromMinutes(1));

        result!.Checkpoint.Should().Be(0);
    }

    [Fact]
    public async Task Descartar_o_checkpoint_libera_o_lock_de_lideranca() {
        var lockHandle = Substitute.For<IAsyncDisposable>();
        _cache.IsLeaderAsync("batch-1", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(lockHandle);

        var result = await _sut.GetCheckpointAsync("batch-1", TimeSpan.FromMinutes(1));
        await result!.DisposeAsync();

        await lockHandle.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task UpdateCheckpointAsync_persiste_a_linha() {
        await _sut.UpdateCheckpointAsync("batch-1", 7);

        await _cache.Received(1).SetStringAsync(
            Arg.Is<string>(k => k.Contains("batch-1") && k.Contains("checkpoint")),
            "7", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkProcessedAsync_deduplica_via_SetIfNotExists() {
        _cache.SetIfNotExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>()).Returns(true, false);

        var first = await _sut.MarkProcessedAsync("batch-1", "uuid-x");
        var second = await _sut.MarkProcessedAsync("batch-1", "uuid-x");

        first.Should().BeTrue("primeira marcação é nova");
        second.Should().BeFalse("repetição é deduplicada — não retorna sempre true como na origem");
    }

    [Fact]
    public async Task MarkProcessedAsync_usa_chave_por_item() {
        _cache.SetIfNotExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>()).Returns(true);

        await _sut.MarkProcessedAsync("batch-1", "uuid-x");

        await _cache.Received(1).SetIfNotExistsAsync(
            Arg.Is<string>(k => k.Contains("batch-1") && k.Contains("uuid-x")),
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncrementProgressAsync_incrementa_de_fato() {
        _cache.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(1L);

        await _sut.IncrementProgressAsync("batch-1");

        await _cache.Received(1).IncrementAsync(
            Arg.Is<string>(k => k.Contains("batch-1") && k.Contains("progress")),
            Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }
}
