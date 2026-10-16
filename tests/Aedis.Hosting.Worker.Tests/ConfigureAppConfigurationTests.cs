using Aedis.Hosting.Worker;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Aedis.Hosting.Worker.Tests;

/// <summary>
///     <c>ConfigureAppConfiguration</c> do worker roda antes dos serviços lerem a configuração, no modo com
///     endpoint de health (o headless usa o mesmo hook no mesmo ponto).
/// </summary>
public sealed class ConfigureAppConfigurationTests
{
    [Fact]
    public void Fonte_adicionada_no_hook_chega_a_ConfigureServices() {
        var host = new HookedWorkerHost();

        using var app = host.BuildWebApplication(["--environment", "Development"], builder => builder.WebHost.UseTestServer());

        app.Configuration["Cofre:Fila"].Should().Be("orders");
        host.ValueSeen.Should().Be("orders");
    }

    private sealed class HookedWorkerHost : AedisWorkerHost
    {
        public string? ValueSeen { get; private set; }

        protected override bool EnableTelemetry => false;

        protected override void ConfigureAppConfiguration(IConfigurationBuilder configuration, IHostEnvironment environment) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Cofre:Fila"] = "orders" });

        protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) =>
            ValueSeen = configuration["Cofre:Fila"];
    }
}
