using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Aedis.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Com autenticação ligada (fallback autenticada), as probes do Kubernetes precisam responder sem token;
///     o <c>/health</c> detalhado continua exigindo autenticação.
/// </summary>
public sealed class HealthProbesAuthorizationTests
{
    private static async Task<WebApplication> StartAsync() {
        var app = new AuthenticatedSampleApiHost().BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });
        await app.StartAsync();
        return app;
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Probes_respondem_sem_token_e_sem_detalhe_interno(string path) {
        await using var app = await StartAsync();

        var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        foreach (var check in body.GetProperty("checks").EnumerateArray()) {
            check.TryGetProperty("description", out _).Should().BeFalse();
            check.TryGetProperty("data", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Health_detalhado_exige_autenticacao() {
        await using var app = await StartAsync();

        var response = await app.GetTestClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

/// <summary>Host com autenticação ligada e um esquema que nunca autentica (requisição anônima).</summary>
public sealed class AuthenticatedSampleApiHost : AedisApiHost
{
    /// <inheritdoc cref="SampleApiHost.EnableTelemetry" />
    protected override bool EnableTelemetry => false;

    /// <inheritdoc />
    protected override void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration) {
        services.AddAuthentication(AnonymousAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AnonymousAuthHandler>(AnonymousAuthHandler.SchemeName, null);
    }

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }
}

/// <summary>Esquema de teste que nunca autentica.</summary>
public sealed class AnonymousAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Nome do esquema.</summary>
    public const string SchemeName = "Test";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
