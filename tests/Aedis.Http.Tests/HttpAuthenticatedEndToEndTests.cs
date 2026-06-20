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

namespace Aedis.Http.Tests;

/// <summary>
///     Exercita a camada HTTP ponta-a-ponta contra um servidor real em loopback — documentação executável do
///     uso: <c>AddAedisAuthenticatedClient</c> registra a integração; o provider nativo obtém o token no
///     endpoint OAuth, o cacheia e anexa o Bearer às chamadas de negócio. Prova que o token é buscado uma
///     única vez para múltiplas chamadas e que a chamada autenticada chega com a credencial correta.
/// </summary>
public sealed class HttpAuthenticatedEndToEndTests
{
    [Fact]
    public async Task Cliente_autenticado_obtem_token_uma_vez_e_chama_o_recurso() {
        var tokenRequests = new int[1];
        await using var server = await StartServerAsync(tokenRequests);
        var baseUrl = BaseUrlOf(server);

        var provider = new ServiceCollection()
            .AddAedisAuthenticatedClient("parceiro", options => {
                options.TokenEndpoint = $"{baseUrl}/oauth/token";
                options.Strategy = new ClientCredentialsBodyStrategy("client", "secret");
                options.Transport = new HttpClientProfile { BaseAddress = baseUrl };
                options.CacheKey = "parceiro:auth:token";
            })
            .BuildServiceProvider();

        var client = provider.GetRequiredKeyedService<IAedisHttpClient>("parceiro");

        var first = await client.SendAsync(AedisHttpRequest.Get("/recurso"));
        var second = await client.SendAsync(AedisHttpRequest.Get("/recurso"));

        first.StatusCode.Should().Be(200);
        first.ReadAsString().Should().Be("autorizado");
        second.StatusCode.Should().Be(200);
        tokenRequests[0].Should().Be(1, "o token é cacheado e reusado entre chamadas");
    }

    private static async Task<WebApplication> StartServerAsync(int[] tokenRequests) {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");

        app.MapPost("/oauth/token", () => {
            Interlocked.Increment(ref tokenRequests[0]);
            return Results.Json(new { access_token = "token-do-servidor", expires_in = 3600 });
        });

        app.MapGet("/recurso", (HttpContext context) =>
            context.Request.Headers.Authorization.ToString() == "Bearer token-do-servidor"
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
