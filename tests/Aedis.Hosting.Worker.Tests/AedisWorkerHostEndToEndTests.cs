using System.Net;
using Aedis.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Aedis.Hosting.Worker.Tests;

/// <summary>
///     Exercita o <see cref="AedisWorkerHost" /> ponta-a-ponta via <see cref="SampleWorkerHost" />: prova que
///     os serviços registrados (um <see cref="IHostedService" />) realmente sobem com o host e que o endpoint
///     de health responde quando habilitado.
/// </summary>
public sealed class AedisWorkerHostEndToEndTests
{
    [Fact]
    public async Task Worker_sobe_os_servicos_registrados_e_expoe_o_health() {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new SampleWorkerHost(started);

        await using var app = host.BuildWebApplication(["--environment", "Development"],
            builder => builder.WebHost.UseTestServer());
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        started.Task.IsCompletedSuccessfully.Should().BeTrue("o hosted service registrado pelo worker foi iniciado");
    }
}

/// <summary>
///     Exemplo mínimo de uso de <see cref="AedisWorkerHost" /> — a documentação executável do host headless.
///     Uma aplicação real herda assim e registra seus consumers/jobs em <c>ConfigureServices</c>; a
///     observabilidade, o health e o shutdown gracioso já vêm compostos. No <c>Main</c> bastaria
///     <c>await new SampleWorkerHost().RunAsync(args);</c>.
/// </summary>
public sealed class SampleWorkerHost(TaskCompletionSource started) : AedisWorkerHost
{
    /// <summary>Desliga a telemetria OTLP no exemplo/teste.</summary>
    protected override bool EnableTelemetry => false;

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddSingleton(started);
        services.AddHostedService<StartupSignalWorker>();
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }

    private sealed class StartupSignalWorker(TaskCompletionSource started) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) {
            started.TrySetResult();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
