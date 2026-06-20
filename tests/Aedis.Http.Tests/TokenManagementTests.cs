using System.Text;
using Aedis.Http.Abstractions;
using Aedis.Http.Abstractions.Authentication;
using Aedis.Http.Authentication;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Http.Tests;

/// <summary>
///     Garante o ciclo de vida do token no <see cref="OAuthTokenProvider" />: get-or-refresh (busca uma vez,
///     reusa do cache), single-flight (uma única busca sob concorrência) e a política de TTL (skew + limites).
/// </summary>
public sealed class OAuthTokenProviderTests
{
    [Fact]
    public async Task Busca_o_token_uma_vez_e_reusa_do_cache() {
        var client = ClientReturning(TokenResponses.Of("abc"));
        var provider = new OAuthTokenProvider(FactoryFor(client), new InMemoryTokenStore(), TokenResponses.Options());

        var first = await provider.GetTokenAsync();
        var second = await provider.GetTokenAsync();

        first.Should().Be("abc");
        second.Should().Be("abc");
        await client.Received(1).SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sob_concorrencia_faz_uma_unica_busca() {
        var client = Substitute.For<IAedisHttpClient>();
        client.SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => { Thread.Sleep(30); return TokenResponses.Of("abc"); });
        var provider = new OAuthTokenProvider(FactoryFor(client), new InMemoryTokenStore(), TokenResponses.Options());

        var tokens = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => provider.GetTokenAsync()));

        tokens.Should().AllBe("abc");
        await client.Received(1).SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(3600, 30)]
    [InlineData(120, 1)]
    [InlineData(36000, 240)]
    public void Calcula_o_ttl_com_skew_e_limites(int expiresInSeconds, int expectedMinutes) {
        var provider = new OAuthTokenProvider(FactoryFor(Substitute.For<IAedisHttpClient>()), new InMemoryTokenStore(), TokenResponses.Options());

        provider.CalculateTtl(expiresInSeconds).Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    private static IAedisHttpClient ClientReturning(AedisHttpResponse response) {
        var client = Substitute.For<IAedisHttpClient>();
        client.SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>()).Returns(response);
        return client;
    }

    private static IAedisHttpClientFactory FactoryFor(IAedisHttpClient client) {
        var factory = Substitute.For<IAedisHttpClientFactory>();
        factory.Create(Arg.Any<HttpClientProfile>()).Returns(client);
        return factory;
    }
}

/// <summary>
///     Garante que o <see cref="AuthenticatedHttpClient" /> anexa o Bearer e, ao receber 401, invalida o
///     token, obtém um novo e reenvia a requisição uma única vez.
/// </summary>
public sealed class AuthenticatedHttpClientTests
{
    [Fact]
    public async Task Anexa_bearer_e_reenvia_uma_vez_no_401() {
        var tokenProvider = Substitute.For<ITokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-1", "token-2");

        var inner = Substitute.For<IAedisHttpClient>();
        inner.SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>())
            .Returns(TokenResponses.Status(401), TokenResponses.Status(200));

        var client = new AuthenticatedHttpClient(inner, tokenProvider);
        var response = await client.SendAsync(AedisHttpRequest.Get("/recurso"));

        response.StatusCode.Should().Be(200);
        await tokenProvider.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
        await inner.Received(2).SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>());
    }
}

/// <summary>Garante que o <see cref="InMemoryTokenStore" /> respeita o TTL: lê o token até expirar e depois devolve null.</summary>
public sealed class InMemoryTokenStoreTests
{
    [Fact]
    public async Task Expira_a_entrada_apos_o_ttl() {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryTokenStore(time);

        await store.SetAsync("k", "tok", TimeSpan.FromMinutes(5));
        (await store.GetAsync("k")).Should().Be("tok");

        time.Advance(TimeSpan.FromMinutes(6));
        (await store.GetAsync("k")).Should().BeNull();
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}

internal static class TokenResponses
{
    public static AedisHttpResponse Of(string accessToken, int expiresIn = 3600) => new() {
        StatusCode = 200,
        Headers = new Dictionary<string, string>(),
        Body = Encoding.UTF8.GetBytes($"{{\"access_token\":\"{accessToken}\",\"expires_in\":{expiresIn}}}")
    };

    public static AedisHttpResponse Status(int statusCode) => new() {
        StatusCode = statusCode,
        Headers = new Dictionary<string, string>(),
        Body = []
    };

    public static OAuthTokenOptions Options() => new() {
        TokenEndpoint = "https://idp.example.com/oauth/token",
        Strategy = new ClientCredentialsBodyStrategy("client", "secret")
    };
}
