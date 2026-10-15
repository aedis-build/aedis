using System.Net;
using Aedis.Signing.Abstractions;
using Aedis.Signing.Verification;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Aedis.Signing.Tests;

/// <summary>JWKS remoto com cache: uma busca por TTL; <c>kid</c> desconhecido força um refetch (rotação).</summary>
public sealed class RemoteJwksKeyResolverTests
{
    private sealed class JwksServer : HttpMessageHandler
    {
        public JsonWebKeySetDocument Document { get; set; } = new();
        public int Fetches { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Fetches++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(Document.ToJson(), System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task Cacheia_por_ttl_e_refaz_a_busca_para_kid_desconhecido() {
        using var oldKey = TestSigningKeys.NewP256();
        using var newKey = TestSigningKeys.NewP256();
        var server = new JwksServer { Document = new JsonWebKeySetDocument { Keys = [TestSigningKeys.Handle(oldKey, "k1").Jwk] } };
        var time = new FakeTimeProvider();
        var resolver = new RemoteJwksKeyResolver(new HttpClient(server), new Uri("https://issuer.example.com/.well-known/jwks.json"),
            TimeSpan.FromMinutes(10), time);

        (await resolver.ResolveAsync("k1")).Should().NotBeNull();
        (await resolver.ResolveAsync("k1")).Should().NotBeNull();
        server.Fetches.Should().Be(1, "dentro do TTL o documento vem do cache");

        server.Document = new JsonWebKeySetDocument { Keys = [TestSigningKeys.Handle(oldKey, "k1").Jwk, TestSigningKeys.Handle(newKey, "k2").Jwk] };
        (await resolver.ResolveAsync("k2")).Should().NotBeNull("kid desconhecido força refetch");
        server.Fetches.Should().Be(2);

        (await resolver.ResolveAsync("k3")).Should().BeNull();
        server.Fetches.Should().Be(3);

        time.Advance(TimeSpan.FromMinutes(11));
        await resolver.ResolveAsync("k1");
        server.Fetches.Should().Be(4, "TTL expirado");
    }
}
