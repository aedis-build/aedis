using System.Net;
using System.Net.Http.Json;
using Aedis.Http.Abstractions;
using Aedis.Signing.Abstractions;
using Aedis.Signing.Verification;
using FluentAssertions;
using Xunit;

namespace Aedis.Signing.Tests;

/// <summary>
///     Integração HTTP agnóstica: o <see cref="HttpSignatureHandler" /> assina a requisição final que passa
///     por qualquer <c>HttpClient</c>, e o <c>HttpClientProfile.MessageHandlers</c> o encaixa logo antes do
///     transporte nos clientes criados pelo perfil.
/// </summary>
public sealed class HttpSignatureHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (request.Content is not null) await request.Content.LoadIntoBufferAsync(cancellationToken);
            Captured = request;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    [Fact]
    public async Task Handler_assina_a_requisicao_que_chega_ao_transporte() {
        using var key = TestSigningKeys.NewP256();
        var (provider, state) = await TestSigningKeys.ReadyInProcessAsync(key);
        var signer = new HttpMessageSigner(provider, provider, new SigningOptions(), state, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var transport = new CapturingHandler();
        using var client = new HttpClient(new HttpSignatureHandler(signer) { InnerHandler = transport });

        var response = await client.PostAsJsonAsync("https://partner.example.com/hooks", new { id = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        transport.Captured!.Headers.Contains("Signature").Should().BeTrue();
        var jwks = new JsonWebKeySetDocument { Keys = [(await provider.GetKeyAsync()).Jwk] };
        var result = await new HttpMessageSignatureVerifier(new StaticJwksKeyResolver(jwks)).VerifyAsync(transport.Captured);
        result.IsValid.Should().BeTrue(result.Detail);
    }

    [Fact]
    public async Task Perfil_encadeia_o_handler_antes_do_transporte() {
        using var key = TestSigningKeys.NewP256();
        var (provider, state) = await TestSigningKeys.ReadyInProcessAsync(key);
        var signer = new HttpMessageSigner(provider, provider, new SigningOptions(), state, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var order = new List<string>();
        var profile = new HttpClientProfile { BaseAddress = "https://partner.example.com/" };
        profile.MessageHandlers.Add(() => new RecordingHandler("externo", order));
        profile.MessageHandlers.Add(() => new HttpSignatureHandler(signer));
        profile.MessageHandlers.Add(() => new RecordingHandler("interno", order));

        using var client = profile.CreateHttpClient();
        var transport = new CapturingHandler();
        var pipeline = (DelegatingHandler)typeof(HttpMessageInvoker).GetField("_handler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(client)!;
        InnermostOf(pipeline).InnerHandler = transport;

        await client.PostAsync("hooks", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        order.Should().Equal("externo", "interno");
        transport.Captured!.Headers.Contains("Signature").Should().BeTrue("o handler interno já vê a requisição assinada");
    }

    private static DelegatingHandler InnermostOf(DelegatingHandler handler) {
        while (handler.InnerHandler is DelegatingHandler inner) handler = inner;
        return handler;
    }

    private sealed class RecordingHandler(string name, List<string> order) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            order.Add(name);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
