using Aedis.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     <see cref="AedisApiHost.RunAsync" /> de ponta a ponta com os defaults do host: autenticação ligada sem
///     provedor (o portão fail-closed fica ativo), telemetria ligada sem endpoint OTLP, Kestrel em porta efêmera e
///     encerramento pelo <see cref="IHostApplicationLifetime" />. Falhas de inicialização são propagadas.
/// </summary>
public sealed class AedisApiHostRunTests
{
    [Fact]
    public async Task RunAsync_sobe_com_os_defaults_e_encerra_no_StopApplication() {
        await new DefaultsApiHost().RunAsync(["--environment", "Development", "--urls", "http://127.0.0.1:0"]);
    }

    [Fact]
    public async Task RunAsync_propaga_falha_fatal_de_inicializacao() {
        await FluentActions.Awaiting(() => new BrokenApiHost().RunAsync(["--environment", "Development"]))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    private sealed class DefaultsApiHost : AedisApiHost
    {
        protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
            services.AddHostedService<StopOnStartedService>();
            services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
        }
    }

    private sealed class BrokenApiHost : AedisApiHost
    {
        protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class StopOnStartedService(IHostApplicationLifetime lifetime) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) {
            lifetime.ApplicationStarted.Register(lifetime.StopApplication);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
