using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Aedis.Signing.Abstractions;
using Aedis.Signing.AspNetCore;
using Aedis.Signing.Verification;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Signing.AspNetCore.Tests;

/// <summary>
///     Rota JWKS num pipeline real (TestServer): publica a chave com cache e ETag (304 em If-None-Match), 404
///     com a assinatura desligada, exige autenticação quando pedido — e um receptor ASP.NET Core verifica uma
///     requisição assinada pelo próprio emissor.
/// </summary>
public sealed class SigningJwksEndpointTests
{
    private static async Task<WebApplication> CreateAppAsync(Dictionary<string, string?> settings, bool allowAnonymous = true, string? path = null) {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, DenyAllHandler>("Test", null);
        builder.Services.AddAuthorization();
        builder.Services.AddAedisSigning(builder.Configuration);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAedisSigningJwks(path, allowAnonymous);
        app.MapPost("/hooks", async (HttpContext context, IJwksProvider jwks) => {
            var received = await context.Request.ToSignedHttpRequestAsync(context.RequestAborted);
            var verifier = new HttpMessageSignatureVerifier(new StaticJwksKeyResolver(await jwks.GetAsync()));
            var result = await verifier.VerifyAsync(received, context.RequestAborted);
            return result.IsValid ? Results.Ok("aceito") : Results.Problem(statusCode: 401, title: result.Failure.ToString(), detail: result.Detail);
        });

        await app.StartAsync();
        return app;
    }

    private static Dictionary<string, string?> Enabled() => new() { ["Signing:InProcess:AllowEphemeralKey"] = "true" };

    [Fact]
    public async Task Publica_jwks_com_cache_e_etag_e_responde_304() {
        await using var app = await CreateAppAsync(Enabled());
        var client = app.GetTestClient();

        var response = await client.GetAsync("/.well-known/jwks.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromHours(1));
        var etag = response.Headers.ETag!.Tag;
        var document = JsonWebKeySetDocument.Parse(await response.Content.ReadAsStringAsync());
        document.Keys.Should().ContainSingle().Which.Kid.Should().StartWith("dev-");

        var conditional = new HttpRequestMessage(HttpMethod.Get, "/.well-known/jwks.json");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        (await client.SendAsync(conditional)).StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Path_customizado_e_respeitado() {
        await using var app = await CreateAppAsync(Enabled(), path: "/v1/pedidos/.well-known/jwks.json");

        (await app.GetTestClient().GetAsync("/v1/pedidos/.well-known/jwks.json")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Desligado_responde_404() {
        await using var app = await CreateAppAsync(new Dictionary<string, string?> { ["Signing:Enabled"] = "false" });

        (await app.GetTestClient().GetAsync("/.well-known/jwks.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sem_anonimo_exige_chamador_autenticado() {
        await using var app = await CreateAppAsync(Enabled(), allowAnonymous: false);

        (await app.GetTestClient().GetAsync("/.well-known/jwks.json")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Receptor_aspnetcore_verifica_requisicao_assinada_pelo_emissor() {
        await using var app = await CreateAppAsync(Enabled());
        var signer = app.Services.GetRequiredService<IHttpMessageSigner>();
        var client = app.GetTestClient();

        var signed = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, "/hooks")) {
            Content = new StringContent("{\"id\":1}", System.Text.Encoding.UTF8, "application/json")
        };
        await signer.SignAsync(signed);
        var accepted = await client.SendAsync(signed);

        var tampered = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, "/hooks")) {
            Content = new StringContent("{\"id\":2}", System.Text.Encoding.UTF8, "application/json")
        };
        foreach (var header in signed.Headers) tampered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        var rejected = await client.SendAsync(tampered);

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("DigestMismatch");
    }

    private sealed class DenyAllHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
