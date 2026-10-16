using Aedis.Hosting.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aedis.Diagnostics.Tests;

/// <summary>Sequência de desligamento: readiness marcada, drenagem, handlers de limpeza e descarte dos recursos.</summary>
public sealed class GracefulShutdownHostedServiceTests
{
    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => _stopping.Cancel();
    }

    private static GracefulShutdownHostedService Create(FakeLifetime lifetime, ShutdownHealthCheck health,
        IDisposableRegistry registry, IEnumerable<IShutdownCleanup> cleanups, TimeSpan? drain = null) =>
        new(lifetime, health, registry, cleanups,
            Options.Create(new GracefulShutdownOptions { DrainDelay = drain ?? TimeSpan.Zero }),
            NullLogger<GracefulShutdownHostedService>.Instance);

    [Fact]
    public async Task Ao_parar_marca_readiness_executa_handlers_e_descarta_recursos() {
        var lifetime = new FakeLifetime();
        var health = new ShutdownHealthCheck();
        var registry = Substitute.For<IDisposableRegistry>();
        var ok = Substitute.For<IShutdownCleanup>();
        var failing = Substitute.For<IShutdownCleanup>();
        failing.CleanupAsync(Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("x"));
        var service = Create(lifetime, health, registry, [failing, ok], TimeSpan.FromMilliseconds(20));
        await service.StartAsync(CancellationToken.None);

        lifetime.StopApplication();

        health.IsShuttingDown.Should().BeTrue();
        await ok.Received(1).CleanupAsync(Arg.Any<CancellationToken>());
        await registry.Received(1).DisposeAllAsync(Arg.Any<CancellationToken>());
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Sem_handlers_e_com_falha_no_registro_nao_lanca() {
        var lifetime = new FakeLifetime();
        var registry = Substitute.For<IDisposableRegistry>();
        registry.DisposeAllAsync(Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("x"));
        var service = Create(lifetime, new ShutdownHealthCheck(), registry, []);
        await service.StartAsync(CancellationToken.None);

        var act = lifetime.StopApplication;

        act.Should().NotThrow();
    }
}
