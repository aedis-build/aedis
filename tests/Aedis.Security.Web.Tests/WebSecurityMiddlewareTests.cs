using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Aedis.Security.Web.Tests;

/// <summary>
///     Verifica que <c>AddAedisWebSecurity</c> + <c>UseAedisWebSecurity</c> aplicam os controles
///     secure-by-default num pipeline real (TestServer): cabeçalhos de segurança presentes, proteção de
///     Host recusando Host inválido e rate limiting respondendo 429 ao exceder o limite.
/// </summary>
public sealed class WebSecurityMiddlewareTests
{
    private static async Task<WebApplication> CreateAppAsync(Dictionary<string, string?> settings) {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddAedisWebSecurity(builder.Configuration);

        var app = builder.Build();
        app.UseAedisWebSecurity();
        app.MapGet("/", () => "ok");

        await app.StartAsync();
        return app;
    }

    private static Dictionary<string, string?> BaseSettings() => new() {
        ["Security:Https:EnableHttpsRedirection"] = "false",
        ["Security:Https:EnableHsts"] = "false"
    };

    [Fact]
    public async Task Resposta_carrega_os_cabecalhos_de_seguranca() {
        await using var app = await CreateAppAsync(BaseSettings());
        var client = app.GetTestClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("no-referrer");
        response.Headers.Contains("Content-Security-Policy").Should().BeTrue();
    }

    [Fact]
    public async Task Host_invalido_e_recusado_com_400() {
        await using var app = await CreateAppAsync(BaseSettings());
        var client = app.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "atacante.example.com";
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Host_explicitamente_permitido_passa() {
        var settings = BaseSettings();
        settings["Security:HostHeaders:AllowedHosts:0"] = "api.example.com";
        await using var app = await CreateAppAsync(settings);
        var client = app.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "api.example.com";
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Excesso_de_requisicoes_retorna_429() {
        var settings = BaseSettings();
        settings["Security:RateLimiting:PermitLimit"] = "2";
        settings["Security:RateLimiting:Window"] = "00:01:00";
        await using var app = await CreateAppAsync(settings);
        var client = app.GetTestClient();

        (await client.GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
