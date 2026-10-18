using Aedis.Security.Web.Middleware;
using Aedis.Security.Web.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Aedis.Security.Web.Tests;

/// <summary>
///     Regras de aceitação do header <c>Host</c>: vazio é recusado; <c>localhost</c>, IP direto (quando permitido),
///     DNS interno do cluster e curinga <c>*.dominio</c> são aceitos; o resto cai em 400 — exceto nos paths de bypass.
/// </summary>
public sealed class HostHeaderProtectionMiddlewareTests
{
    private static async Task<int> StatusFor(string host, Action<HostHeaderProtectionOptions>? configure = null, string path = "/api") {
        var options = new HostHeaderProtectionOptions();
        configure?.Invoke(options);
        var middleware = new HostHeaderProtectionMiddleware(_ => Task.CompletedTask, options);
        var context = new DefaultHttpContext();
        context.Request.Host = host.Length == 0 ? default : new HostString(host);
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        return context.Response.StatusCode;
    }

    [Theory]
    [InlineData("", 400)]
    [InlineData("localhost", 200)]
    [InlineData("10.0.0.7", 200)]
    [InlineData("orders.shop.svc.cluster.local", 200)]
    [InlineData("api.example.com", 200)]
    [InlineData("x.api.example.com", 200)]
    [InlineData("api.example.com.evil.io", 400)]
    [InlineData("evil.io", 400)]
    public async Task Decide_pelo_host(string host, int expected) {
        (await StatusFor(host, o => { o.AllowedHosts.Add("api.example.com"); o.AllowedHosts.Add("*.api.example.com"); }))
            .Should().Be(expected);
    }

    [Fact]
    public async Task IP_direto_e_recusado_quando_desligado_e_paths_de_bypass_passam_sempre() {
        (await StatusFor("10.0.0.7", o => o.AllowDirectIpAccess = false)).Should().Be(400);
        (await StatusFor("evil.io", path: "/health/ready")).Should().Be(200);
        (await StatusFor("evil.io", o => o.Enabled = false)).Should().Be(200);
    }
}
