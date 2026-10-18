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

    [Fact]
    public async Task RunAsync_headless_sobe_com_telemetria_padrao_e_encerra_no_StopApplication() {
        await new StopOnStartedWorkerHost(healthEndpoint: false).RunAsync(["--environment", "Development"]);
    }

    [Fact]
    public async Task RunAsync_com_endpoint_de_health_em_porta_efemera_encerra_no_StopApplication() {
        await new StopOnStartedWorkerHost(healthEndpoint: true).RunAsync(["--environment", "Development", "--urls", "http://127.0.0.1:0"]);
    }

    [Fact]
    public async Task RunAsync_propaga_falha_fatal_de_inicializacao() {
        await FluentActions.Awaiting(() => new BrokenWorkerHost().RunAsync(["--environment", "Development"]))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}

/// <summary>Worker com os defaults do host (telemetria ligada, sem endpoint OTLP) que pede parada assim que termina de subir.</summary>
public sealed class StopOnStartedWorkerHost(bool healthEndpoint) : AedisWorkerHost
{
    /// <inheritdoc />
    protected override bool EnableHealthEndpoint => healthEndpoint;

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddHostedService<StopOnStartedService>();
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }
}

/// <summary>Worker cujo registro de serviços falha: o host deve logar e propagar.</summary>
public sealed class BrokenWorkerHost : AedisWorkerHost
{
    /// <inheritdoc />
    protected override bool EnableHealthEndpoint => false;

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) =>
        throw new InvalidOperationException("boom");
}

/// <summary>Pede o encerramento da aplicação logo após ela terminar de iniciar.</summary>
public sealed class StopOnStartedService(IHostApplicationLifetime lifetime) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) {
        lifetime.ApplicationStarted.Register(lifetime.StopApplication);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
