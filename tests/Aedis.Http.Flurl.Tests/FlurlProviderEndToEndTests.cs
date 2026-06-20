using Aedis.Http.Abstractions;
using Aedis.Http.Abstractions.Authentication;
using Aedis.Http.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aedis.Http.Flurl.Tests;

/// <summary>
///     Prova que o provider Flurl funciona ponta-a-ponta com o mesmo template autenticado: trocando apenas a
///     fábrica via <c>AddAedisHttpFlurl</c>, o fluxo (obter token → anexar Bearer → chamar recurso) opera
///     idêntico ao provider nativo, validando o contrato agnóstico.
/// </summary>
public sealed class FlurlProviderEndToEndTests
{
    [Fact]
    public async Task Integracao_autenticada_funciona_sobre_o_flurl() {
        await using var server = await StartServerAsync();
        var baseUrl = BaseUrlOf(server);

        var provider = new ServiceCollection()
            .AddAedisAuthenticatedClient("parceiro", options => {
                options.TokenEndpoint = $"{baseUrl}/oauth/token";
                options.Strategy = new BasicAuthenticationStrategy("user", "pass");
                options.Transport = new HttpClientProfile { BaseAddress = baseUrl };
                options.CacheKey = "parceiro:auth:token";
            })
            .AddAedisHttpFlurl()
            .BuildServiceProvider();

        var client = provider.GetRequiredKeyedService<IAedisHttpClient>("parceiro");
        var response = await client.SendAsync(AedisHttpRequest.Get("/recurso"));

        response.StatusCode.Should().Be(200);
        response.ReadAsString().Should().Be("autorizado");
    }

    private static async Task<WebApplication> StartServerAsync() {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");

        app.MapPost("/oauth/token", () => Results.Json(new { access_token = "token-flurl", expires_in = 3600 }));
        app.MapGet("/recurso", (HttpContext context) =>
            context.Request.Headers.Authorization.ToString() == "Bearer token-flurl"
                ? Results.Text("autorizado")
                : Results.Unauthorized());

        await app.StartAsync();
        return app;
    }

    private static string BaseUrlOf(WebApplication app) {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        return addresses!.Addresses.First();
    }
}
