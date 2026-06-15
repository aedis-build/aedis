using Aedis.Hosting.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Aedis.Diagnostics.Tests;

/// <summary>
///     Convenção de health checks do Aedis: <c>self</c>/<c>uptime</c> em <c>live</c> e <c>shutdown</c>
///     em <c>ready</c>, registrados por <c>AddAedisDiagnostics()</c>.
/// </summary>
public sealed class DiagnosticsRegistrationTests
{
    private static HealthCheckService BuildHealthCheckService() {
        var services = new ServiceCollection().AddLogging();
        services.AddAedisDiagnostics();
        return services.BuildServiceProvider().GetRequiredService<HealthCheckService>();
    }

    [Fact]
    public async Task Live_contem_self_e_uptime() {
        var service = BuildHealthCheckService();

        var report = await service.CheckHealthAsync(r => r.Tags.Contains("live"));

        report.Entries.Keys.Should().BeEquivalentTo("self", "uptime");
        report.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Ready_contem_o_shutdown() {
        var service = BuildHealthCheckService();

        var report = await service.CheckHealthAsync(r => r.Tags.Contains("ready"));

        report.Entries.Keys.Should().Contain("shutdown");
        report.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void Registra_IDisposableRegistry_como_singleton() {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddAedisDiagnostics()
            .BuildServiceProvider();

        var first = provider.GetRequiredService<IDisposableRegistry>();
        var second = provider.GetRequiredService<IDisposableRegistry>();

        first.Should().BeOfType<DisposableRegistry>();
        first.Should().BeSameAs(second, "o registro é singleton — um por processo");
    }
}
