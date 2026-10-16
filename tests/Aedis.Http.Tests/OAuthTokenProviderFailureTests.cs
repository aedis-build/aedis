using Aedis.Http.Abstractions;
using Aedis.Http.Abstractions.Authentication;
using Aedis.Http.Authentication;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Http.Tests;

/// <summary>Validação das opções, respostas inválidas do endpoint, espera por outra instância, invalidação e renovação em segundo plano.</summary>
public sealed class OAuthTokenProviderFailureTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IAedisHttpClientFactory FactoryFor(IAedisHttpClient client) {
        var factory = Substitute.For<IAedisHttpClientFactory>();
        factory.Create(Arg.Any<HttpClientProfile>()).Returns(client);
        return factory;
    }

    private static IAedisHttpClient ClientReturning(params AedisHttpResponse[] responses) {
        var client = Substitute.For<IAedisHttpClient>();
        client.SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>()).Returns(responses[0], responses[1..]);
        return client;
    }

    [Fact]
    public void Endpoint_e_estrategia_sao_obrigatorios() {
        var factory = FactoryFor(Substitute.For<IAedisHttpClient>());
        var semEndpoint = TokenResponses.Options();
        semEndpoint.TokenEndpoint = " ";
        var semEstrategia = TokenResponses.Options();
        semEstrategia.Strategy = null;

        var a = () => new OAuthTokenProvider(factory, new InMemoryTokenStore(), semEndpoint);
        var b = () => new OAuthTokenProvider(factory, new InMemoryTokenStore(), semEstrategia);

        a.Should().Throw<ArgumentException>().WithMessage("*TokenEndpoint*");
        b.Should().Throw<ArgumentException>().WithMessage("*Strategy*");
    }

    [Fact]
    public async Task Resposta_de_erro_e_resposta_sem_access_token_falham() {
        var erro = new OAuthTokenProvider(FactoryFor(ClientReturning(TokenResponses.Status(500))), new InMemoryTokenStore(), TokenResponses.Options());
        var semToken = new OAuthTokenProvider(FactoryFor(ClientReturning(new AedisHttpResponse {
            StatusCode = 200, Headers = new Dictionary<string, string>(), Body = "{\"expires_in\":10}"u8.ToArray()
        })), new InMemoryTokenStore(), TokenResponses.Options());

        var erroAct = () => erro.GetTokenAsync();
        var semTokenAct = () => semToken.GetTokenAsync();

        await erroAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*500*");
        await semTokenAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*access_token*");
    }

    [Fact]
    public async Task Invalidar_remove_o_token_e_forca_nova_busca() {
        var client = ClientReturning(TokenResponses.Of("t1"), TokenResponses.Of("t2"));
        var provider = new OAuthTokenProvider(FactoryFor(client), new InMemoryTokenStore(), TokenResponses.Options());

        (await provider.GetTokenAsync()).Should().Be("t1");
        await provider.InvalidateAsync();
        (await provider.GetTokenAsync()).Should().Be("t2");
    }

    [Fact]
    public async Task Sem_o_lock_espera_o_token_gerado_por_outra_instancia() {
        var store = Substitute.For<ITokenStore>();
        store.TryAcquireRefreshLockAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IAsyncDisposable?>(null));
        var calls = 0;
        store.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(++calls < 4 ? null : new CachedToken("de-outra", Start.AddYears(10), Start.AddYears(10))));
        var client = Substitute.For<IAedisHttpClient>();
        var provider = new OAuthTokenProvider(FactoryFor(client), store, TokenResponses.Options());

        (await provider.GetTokenAsync()).Should().Be("de-outra");

        await client.DidNotReceive().SendAsync(Arg.Any<AedisHttpRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sem_o_lock_e_sem_token_de_outra_instancia_busca_mesmo_assim() {
        var store = Substitute.For<ITokenStore>();
        store.TryAcquireRefreshLockAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IAsyncDisposable?>(null));
        store.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<CachedToken?>(null));
        var client = ClientReturning(TokenResponses.Of("buscado"));
        var provider = new OAuthTokenProvider(FactoryFor(client), store, TokenResponses.Options());

        (await provider.GetTokenAsync()).Should().Be("buscado");

        await store.Received(1).SetAsync(Arg.Any<string>(), Arg.Is<CachedToken>(t => t.AccessToken == "buscado"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Token_na_janela_de_renovacao_e_devolvido_e_renovado_em_segundo_plano() {
        var time = new FakeTimeProvider(Start);
        var store = new InMemoryTokenStore(time);
        await store.SetAsync(TokenResponses.Options().CacheKey, new CachedToken("velho", Start.AddHours(1), Start.AddMinutes(-1)));
        var client = ClientReturning(TokenResponses.Of("novo"));
        var provider = new OAuthTokenProvider(FactoryFor(client), store, TokenResponses.Options(), time);

        (await provider.GetTokenAsync()).Should().Be("velho", "o token ainda vale; a renovação acontece em segundo plano");

        for (var i = 0; i < 100 && (await store.GetAsync(TokenResponses.Options().CacheKey))!.AccessToken != "novo"; i++)
            await Task.Delay(20);
        (await store.GetAsync(TokenResponses.Options().CacheKey))!.AccessToken.Should().Be("novo");
    }

    [Fact]
    public async Task Falha_na_renovacao_em_segundo_plano_nao_derruba_o_chamador() {
        var time = new FakeTimeProvider(Start);
        var store = new InMemoryTokenStore(time);
        await store.SetAsync(TokenResponses.Options().CacheKey, new CachedToken("velho", Start.AddHours(1), Start.AddMinutes(-1)));
        var client = ClientReturning(TokenResponses.Status(503));
        var provider = new OAuthTokenProvider(FactoryFor(client), store, TokenResponses.Options(), time);

        (await provider.GetTokenAsync()).Should().Be("velho");
        await Task.Delay(100);
        (await provider.GetTokenAsync()).Should().Be("velho");
    }
}
