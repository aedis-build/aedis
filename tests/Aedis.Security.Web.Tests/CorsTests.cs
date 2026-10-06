using System.Net;
using Aedis.Security.Web.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Security.Web.Tests;

/// <summary>
///     CORS da camada de segurança: desligado por padrão (nenhum cabeçalho <c>Access-Control-*</c>), opt-in
///     por lista de origens com preflight respondido só para origem permitida, e validação fail-closed na
///     subida — sem origens ou credenciais com <c>*</c> derrubam o host.
/// </summary>
public sealed class CorsTests
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
        ["Security:Https:EnableHsts"] = "false",
        ["Security:HostHeaders:AllowedHosts:0"] = "localhost"
    };

    private static HttpRequestMessage Preflight(string origin) {
        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "QUERY");
        return request;
    }

    [Fact]
    public async Task Desligado_por_padrao_nao_emite_cabecalhos_cors() {
        await using var app = await CreateAppAsync(BaseSettings());
        var client = app.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "https://app.example.com");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Ligado_responde_preflight_para_origem_permitida_incluindo_QUERY() {
        var settings = BaseSettings();
        settings["Security:Cors:Enabled"] = "true";
        settings["Security:Cors:AllowedOrigins:0"] = "https://app.example.com";
        await using var app = await CreateAppAsync(settings);
        var client = app.GetTestClient();

        var response = await client.SendAsync(Preflight("https://app.example.com"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("https://app.example.com");
        response.Headers.GetValues("Access-Control-Allow-Methods").Should().ContainSingle().Which.Should().Contain("QUERY");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task Ligado_ignora_origem_nao_listada() {
        var settings = BaseSettings();
        settings["Security:Cors:Enabled"] = "true";
        settings["Security:Cors:AllowedOrigins:0"] = "https://app.example.com";
        await using var app = await CreateAppAsync(settings);
        var client = app.GetTestClient();

        var response = await client.SendAsync(Preflight("https://atacante.example.net"));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Curinga_de_subdominio_aceita_subdominios_da_origem() {
        var settings = BaseSettings();
        settings["Security:Cors:Enabled"] = "true";
        settings["Security:Cors:AllowedOrigins:0"] = "https://*.example.com";
        await using var app = await CreateAppAsync(settings);
        var client = app.GetTestClient();

        var response = await client.SendAsync(Preflight("https://painel.example.com"));

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("https://painel.example.com");
    }

    [Fact]
    public void Ligado_sem_origens_falha_a_validacao() {
        var options = new WebSecurityOptions { Cors = { Enabled = true } };

        var result = Validate(options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AllowedOrigins");
    }

    [Fact]
    public void Credenciais_com_origem_curinga_falham_a_validacao() {
        var options = new WebSecurityOptions { Cors = { Enabled = true, AllowCredentials = true, AllowedOrigins = ["*"] } };

        var result = Validate(options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AllowCredentials");
    }

    [Fact]
    public void Desligado_nao_valida_nada() {
        Validate(new WebSecurityOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Host_nao_sobe_com_cors_ligado_sem_origens() {
        var settings = BaseSettings();
        settings["Security:Cors:Enabled"] = "true";

        var act = async () => await CreateAppAsync(settings);

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    private static ValidateOptionsResult Validate(WebSecurityOptions options) =>
        new WebSecurityOptionsValidator().Validate(name: null, options);
}
