using Aedis.Hosting.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     <c>ConfigureAppConfiguration</c> roda antes de a infraestrutura ler a configuração: o que o app adiciona
///     ali (ex.: segredos do cofre) já está disponível para as opções e para <c>ConfigureServices</c>.
/// </summary>
public sealed class ConfigureAppConfigurationTests
{
    [Fact]
    public void Fonte_adicionada_no_hook_chega_a_configuracao_do_host() {
        var host = new HookedApiHost();

        using var app = host.BuildApplication(["--environment", "Development"], builder => builder.WebHost.UseTestServer());

        app.Configuration["Cofre:ConnectionString"].Should().Be("do-cofre");
        host.EnvironmentSeen.Should().Be("Development");
        host.ValueSeenInConfigureServices.Should().Be("do-cofre");
    }

    private sealed class HookedApiHost : AedisApiHost
    {
        public string? EnvironmentSeen { get; private set; }
        public string? ValueSeenInConfigureServices { get; private set; }

        protected override bool EnableAuthentication => false;
        protected override bool EnableTelemetry => false;

        protected override void ConfigureAppConfiguration(IConfigurationBuilder configuration, IHostEnvironment environment) {
            EnvironmentSeen = environment.EnvironmentName;
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Cofre:ConnectionString"] = "do-cofre" });
        }

        protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) =>
            ValueSeenInConfigureServices = configuration["Cofre:ConnectionString"];

        protected override void ConfigureMiddleware(WebApplication app) { }
    }
}
