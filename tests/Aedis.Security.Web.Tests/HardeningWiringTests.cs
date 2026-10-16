using System.Net;
using Aedis.Security.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aedis.Security.Web.Tests;

/// <summary>Kestrel, HSTS, proxies conhecidos, CORS aberto/com credenciais e endpoints de revogação de token.</summary>
public sealed class HardeningWiringTests
{
    private static WebApplicationBuilder Builder(Dictionary<string, string?> settings) {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    private static Dictionary<string, string?> BaseSettings() => new() {
        ["Security:Https:EnableHttpsRedirection"] = "false",
        ["Security:Https:EnableHsts"] = "false",
        ["Security:HostHeaders:AllowedHosts:0"] = "localhost"
    };

    [Fact]
    public void Kestrel_recebe_os_limites_configurados_e_pode_ser_desligado() {
        var settings = BaseSettings();
        settings["Security:Kestrel:MaxRequestBodySizeBytes"] = "1234";
        settings["Security:Kestrel:MaxRequestHeaderCount"] = "12";
        settings["Security:Kestrel:RemoveServerHeader"] = "false";
        var builder = Builder(settings).ConfigureAedisKestrelHardening();
        var kestrel = builder.Build().Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        kestrel.Limits.MaxRequestBodySize.Should().Be(1234);
        kestrel.Limits.MaxRequestHeaderCount.Should().Be(12);
        kestrel.AddServerHeader.Should().BeTrue();
        kestrel.AllowSynchronousIO.Should().BeFalse();

        var off = BaseSettings();
        off["Security:Kestrel:Enabled"] = "false";
        Builder(off).ConfigureAedisKestrelHardening().Build().Services
            .GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestHeaderCount.Should().Be(100);
    }

    [Fact]
    public void Hsts_e_proxies_conhecidos_vem_da_configuracao() {
        var settings = BaseSettings();
        settings["Security:Https:EnableHsts"] = "true";
        settings["Security:Https:HstsMaxAgeDays"] = "10";
        settings["Security:Https:HstsIncludeSubDomains"] = "false";
        settings["Security:Https:HstsPreload"] = "true";
        settings["Security:ForwardedHeaders:Enabled"] = "true";
        settings["Security:ForwardedHeaders:TrustAllProxies"] = "false";
        settings["Security:ForwardedHeaders:KnownProxies:0"] = "10.0.0.1";
        settings["Security:ForwardedHeaders:KnownProxies:1"] = "nao-e-ip";
        var builder = Builder(settings);
        builder.Services.AddAedisWebSecurity(builder.Configuration);
        var services = builder.Build().Services;

        var hsts = services.GetRequiredService<IOptions<HstsOptions>>().Value;
        hsts.MaxAge.Should().Be(TimeSpan.FromDays(10));
        hsts.IncludeSubDomains.Should().BeFalse();
        hsts.Preload.Should().BeTrue();
        services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value.KnownProxies
            .Should().Contain(IPAddress.Parse("10.0.0.1")).And.NotContain(p => p.ToString().Contains("nao"));
    }

    [Fact]
    public async Task Cors_com_origem_curinga_responde_a_qualquer_origem() {
        var settings = BaseSettings();
        settings["Security:Cors:Enabled"] = "true";
        settings["Security:Cors:AllowedOrigins:0"] = "*";
        var builder = Builder(settings);
        builder.Services.AddAedisWebSecurity(builder.Configuration);
        await using var app = builder.Build();
        app.UseAedisWebSecurity();
        app.MapGet("/", () => "ok");
        await app.StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "https://qualquer.example.org");

        var response = await app.GetTestClient().SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("*");
    }

    [Fact]
    public async Task Cors_com_credenciais_emite_allow_credentials_para_a_origem_permitida() {
        var settings = BaseSettings();
        settings["Security:Cors:Enabled"] = "true";
        settings["Security:Cors:AllowedOrigins:0"] = "https://app.example.com";
        settings["Security:Cors:AllowCredentials"] = "true";
        var builder = Builder(settings);
        builder.Services.AddAedisWebSecurity(builder.Configuration);
        await using var app = builder.Build();
        app.UseAedisWebSecurity();
        app.MapGet("/", () => "ok");
        await app.StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "https://app.example.com");

        var response = await app.GetTestClient().SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Credentials").Should().ContainSingle().Which.Should().Be("true");
    }

    [Fact]
    public async Task Endpoints_de_revogacao_chamam_o_servico_e_respondem_204() {
        var revocation = Substitute.For<ITokenRevocation>();
        var builder = Builder(BaseSettings());
        builder.Services.AddAedisWebSecurity(builder.Configuration);
        builder.Services.AddSingleton(revocation);
        builder.Services.AddAuthorization(o => o.AddPolicy("aberta", p => p.RequireAssertion(_ => true)));
        await using var app = builder.Build();
        app.UseAedisWebSecurity();
        app.UseAuthorization();
        app.MapAedisTokenRevocation("aberta");
        await app.StartAsync();
        var client = app.GetTestClient();

        (await client.PostAsync("/security/tokens/revoke/t-1", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsync("/security/tokens/revoke-user/u-1", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await revocation.Received(1).RevokeTokenAsync("t-1", Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await revocation.Received(1).RevokeUserAsync("u-1", Arg.Any<CancellationToken>());
    }
}
