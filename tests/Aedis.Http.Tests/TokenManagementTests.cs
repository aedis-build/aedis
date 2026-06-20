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
///     reusa do cache), single-flight (uma única busca sob concorrência), a renovação proativa antes da
///     expiração e o respeito ao lock de geração (não gera quando outro detém o lock).
/// </summary>
public sealed class OAuthTokenProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
    [InlineData(3600, 60, 30)]
    [InlineData(120, 2, 1)]
    [InlineData(36000, 600, 240)]
    public void Carimba_a_expiracao_e_o_ponto_de_renovacao(int expiresInSeconds, int expiryMinutes, int refreshMinutes) {
        var time = new FakeTimeProvider(Start);
        var provider = new OAuthTokenProvider(FactoryFor(Substitute.For<IAedisHttpClient>()), new InMemoryTokenStore(time), TokenResponses.Options(), time);

        var token = provider.StampToken("t", expiresInSeconds);

        token.ExpiresAt.Should().Be(Start.AddMinutes(expiryMinutes));
        token.RefreshAt.Should().Be(Start.AddMinutes(refreshMinutes));
    }

    [Fact]
    public async Task Renova_proativamente_ao_entrar_na_janela_antes_de_expirar() {
        var time = new FakeTimeProvider(Start);
        var store = new InMemoryTokenStore(time);
        var client = Substitute.For<IAedisHttpClient>();
        client.SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>())
            .Returns(TokenResponses.Of("token-1"), TokenResponses.Of("token-2"));
        var provider = new OAuthTokenProvider(FactoryFor(client), store, TokenResponses.Options(), time);

        (await provider.GetTokenAsync()).Should().Be("token-1");

        time.Advance(TimeSpan.FromMinutes(31));
        await provider.RefreshAsync();

        (await provider.GetTokenAsync()).Should().Be("token-2", "o token foi renovado proativamente ainda válido");
        await client.Received(2).SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nao_gera_token_quando_outro_detem_o_lock_de_geracao() {
        var store = Substitute.For<ITokenStore>();
        store.TryAcquireRefreshLockAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IAsyncDisposable?>(null));
        var client = Substitute.For<IAedisHttpClient>();
        var provider = new OAuthTokenProvider(FactoryFor(client), store, TokenResponses.Options());

        await provider.RefreshAsync();

        await client.DidNotReceive().SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>());
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

/// <summary>Garante que o <see cref="InMemoryTokenStore" /> respeita a expiração do <see cref="CachedToken" />.</summary>
public sealed class InMemoryTokenStoreTests
{
    [Fact]
    public async Task Devolve_o_token_ate_expirar_e_depois_null() {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(start);
        var store = new InMemoryTokenStore(time);
        var token = new CachedToken("tok", start.AddMinutes(5), start.AddMinutes(4));

        await store.SetAsync("k", token);
        (await store.GetAsync("k"))!.AccessToken.Should().Be("tok");

        time.Advance(TimeSpan.FromMinutes(6));
        (await store.GetAsync("k")).Should().BeNull();
    }
}

internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now += delta;
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
