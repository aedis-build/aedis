using System.Net;
using Aedis.Http.Abstractions;
using Aedis.Http.Native;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aedis.Http.Tests;

/// <summary>
///     Garante a proteção SSRF do transporte: a <see cref="SsrfPolicy" /> classifica endereços internos
///     (loopback, redes privadas, link-local/metadata) e o <c>ConnectCallback</c> recusa de fato a conexão a
///     um destino interno — mas libera quando o host está na allowlist (serviço interno legítimo).
/// </summary>
public sealed class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    public void Classifica_enderecos_internos_e_metadata(string ip, bool blocked) {
        new SsrfPolicy { Enabled = true }.IsAddressBlocked(IPAddress.Parse(ip)).Should().Be(blocked);
    }

    [Fact]
    public void Respeita_allowlist_e_blocklist_de_host() {
        var policy = new SsrfPolicy { Enabled = true };
        policy.AllowedHosts.Add("interno.svc");
        policy.BlockedHosts.Add("proibido.com");

        policy.IsHostAllowlisted("interno.svc").Should().BeTrue();
        policy.IsHostBlocked("proibido.com").Should().BeTrue();
    }

    [Fact]
    public async Task Recusa_conexao_a_endereco_interno() {
        var profile = new HttpClientProfile { Ssrf = { Enabled = true } };
        var client = new NativeHttpClientFactory().Create(profile);

        var act = async () => await client.SendAsync(AedisHttpRequest.Get("http://127.0.0.1:1/"));

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        thrown.GetBaseException().Should().BeOfType<SsrfBlockedException>();
    }

    [Fact]
    public async Task Libera_host_interno_na_allowlist() {
        await using var server = await StartLoopbackServerAsync();
        var baseUrl = BaseUrlOf(server);

        var profile = new HttpClientProfile { Ssrf = { Enabled = true } };
        profile.Ssrf.AllowedHosts.Add("127.0.0.1");
        var client = new NativeHttpClientFactory().Create(profile);

        var response = await client.SendAsync(AedisHttpRequest.Get($"{baseUrl}/ping"));

        response.StatusCode.Should().Be(200);
    }

    private static async Task<WebApplication> StartLoopbackServerAsync() {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapGet("/ping", () => Results.Text("pong"));

        await app.StartAsync();
        return app;
    }

    private static string BaseUrlOf(WebApplication app) {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        return addresses!.Addresses.First();
    }
}
