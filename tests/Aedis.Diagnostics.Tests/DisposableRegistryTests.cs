using Aedis.Diagnostics;
using Aedis.Hosting.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Diagnostics.Tests;

/// <summary>
///     Garante o mecanismo que mantém os <em>CacheLocks</em> seguros: o registro descarta os recursos
///     (handles de liderança) no desligamento, e o wrapper por chave se auto-desregistra quando o
///     recurso é liberado antes — sem vazar nem descartar duas vezes.
/// </summary>
public sealed class DisposableRegistryTests
{
    [Fact]
    public async Task DisposeAllAsync_descarta_recursos_sync_async_e_por_chave() {
        var registry = new DisposableRegistry();
        var sync = new TrackingDisposable();
        var async = new TrackingAsyncDisposable();
        var keyed = new TrackingAsyncDisposable();

        registry.Register(sync);
        registry.Register(async);
        registry.Register("lock:leader", keyed);
        registry.Count.Should().Be(3);

        await registry.DisposeAllAsync();

        sync.DisposeCount.Should().Be(1);
        async.DisposeCount.Should().Be(1);
        keyed.DisposeCount.Should().Be(1);
        registry.Count.Should().Be(0, "o registro é esvaziado após o descarte");
    }

    [Fact]
    public async Task Wrapper_por_chave_descarta_o_interno_e_se_auto_desregistra() {
        var registry = new DisposableRegistry();
        var leaderLock = new TrackingAsyncDisposable();

        var handle = registry.Register("lock:leader", leaderLock);
        registry.Count.Should().Be(1);

        await handle.DisposeAsync();

        leaderLock.DisposeCount.Should().Be(1, "o lock real é liberado");
        registry.Count.Should().Be(0, "o recurso some do registro ao ser liberado — sem vazamento");
    }

    [Fact]
    public async Task Wrapper_por_chave_e_idempotente() {
        var registry = new DisposableRegistry();
        var leaderLock = new TrackingAsyncDisposable();

        var handle = registry.Register("lock:leader", leaderLock);
        await handle.DisposeAsync();
        await handle.DisposeAsync();

        leaderLock.DisposeCount.Should().Be(1, "descartes repetidos não liberam o lock duas vezes");
    }

    [Fact]
    public async Task Apos_auto_desregistro_o_DisposeAllAsync_nao_descarta_de_novo() {
        var registry = new DisposableRegistry();
        var leaderLock = new TrackingAsyncDisposable();

        var handle = registry.Register("lock:leader", leaderLock);
        await handle.DisposeAsync();

        await registry.DisposeAllAsync();

        leaderLock.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAllAsync_continua_apesar_de_uma_falha_individual() {
        var registry = new DisposableRegistry();
        var faulty = new TrackingAsyncDisposable { Throw = true };
        var healthy = new TrackingAsyncDisposable();

        registry.Register(faulty);
        registry.Register(healthy);

        var act = async () => await registry.DisposeAllAsync();

        await act.Should().NotThrowAsync("uma falha de descarte não impede os demais");
        healthy.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Register_por_chave_em_branco_lanca() {
        var registry = new DisposableRegistry();

        var act = () => registry.Register(" ", new TrackingAsyncDisposable());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Registro_concorrente_e_thread_safe() {
        var registry = new DisposableRegistry();
        var locks = Enumerable.Range(0, 200).Select(_ => new TrackingAsyncDisposable()).ToArray();

        await Parallel.ForEachAsync(locks, (item, _) => {
            registry.Register(item);
            return ValueTask.CompletedTask;
        });

        registry.Count.Should().Be(200);

        await registry.DisposeAllAsync();

        locks.Should().OnlyContain(l => l.DisposeCount == 1);
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public bool Throw { get; init; }

        public ValueTask DisposeAsync() {
            DisposeCount++;
            if (Throw) throw new InvalidOperationException("falha simulada no descarte");
            return ValueTask.CompletedTask;
        }
    }
}
