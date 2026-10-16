using Aedis.Scheduling.Hangfire;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Dashboard;
using Hangfire.Server;
using Hangfire.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aedis.Scheduling.Hangfire.Tests;

/// <summary>Filtro de execução exclusiva (lock distribuído), filtro do dashboard e extensão do dashboard desligado.</summary>
public sealed class HangfireFiltersTests
{
    private static class Jobs
    {
        public static void Work() { }
    }

    private static PerformContext Context(IStorageConnection connection) {
        var job = new BackgroundJob("1", Job.FromExpression(() => Jobs.Work()), DateTime.UtcNow);
        return new PerformContext(Substitute.For<JobStorage>(), connection, job, Substitute.For<IJobCancellationToken>());
    }

    [Fact]
    public void Lock_adquirido_e_guardado_e_liberado_ao_terminar() {
        var connection = Substitute.For<IStorageConnection>();
        var handle = Substitute.For<IDisposable>();
        connection.AcquireDistributedLock(Arg.Any<string>(), TimeSpan.Zero).Returns(handle);
        var filter = new SkipConcurrentExecutionAttribute();
        var performing = new PerformingContext(Context(connection));

        filter.OnPerforming(performing);
        filter.OnPerformed(new PerformedContext(performing, null, false, null));

        performing.Canceled.Should().BeFalse();
        connection.Received(1).AcquireDistributedLock(Arg.Is<string>(r => r.EndsWith(".Work")), TimeSpan.Zero);
        handle.Received(1).Dispose();
    }

    [Fact]
    public void Sem_conseguir_o_lock_a_execucao_e_cancelada() {
        var connection = Substitute.For<IStorageConnection>();
        connection.AcquireDistributedLock(Arg.Any<string>(), TimeSpan.Zero)
            .Returns(_ => throw new DistributedLockTimeoutException("r"));
        var filter = new SkipConcurrentExecutionAttribute();
        var performing = new PerformingContext(Context(connection));

        filter.OnPerforming(performing);
        filter.OnPerformed(new PerformedContext(performing, null, true, null));

        performing.Canceled.Should().BeTrue();
    }

    [Fact]
    public void Dashboard_desligado_nao_registra_middleware_e_filtro_libera_todos() {
        var services = new ServiceCollection().Configure<HangfireOptions>(o => o.EnableDashboard = false).BuildServiceProvider();
        var app = Substitute.For<IApplicationBuilder>();
        app.ApplicationServices.Returns(services);

        app.UseAedisHangfireDashboard().Should().BeSameAs(app);

        app.DidNotReceiveWithAnyArgs().Use(default!);
        new AllowAllDashboardAuthorizationFilter().Authorize(Substitute.For<DashboardContext>(Substitute.For<JobStorage>(), new DashboardOptions()))
            .Should().BeTrue();
    }
}
