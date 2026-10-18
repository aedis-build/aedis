using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aedis.Http.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Http.Tests;

/// <summary>
///     <see cref="HttpClientProfileExtensions.CreateHttpClient" />: aplica certificado de cliente e cabeçalhos
///     padrão; com SSRF ligado, host da lista de bloqueio é recusado antes do DNS e uma falha de conexão a um host
///     permitido descarta o socket e propaga.
/// </summary>
public sealed class HttpClientProfileExtensionsTests
{
    [Fact]
    public async Task Aplica_certificado_e_cabecalhos_recusa_host_bloqueado_e_propaga_falha_de_conexao() {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=aedis-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var profile = new HttpClientProfile { ClientCertificates = [certificate], Ssrf = { Enabled = true } };
        profile.DefaultHeaders["X-Trace"] = "1";
        profile.Ssrf.BlockedHosts.Add("metadata.internal");
        profile.Ssrf.AllowedHosts.Add("127.0.0.1");

        using var client = profile.CreateHttpClient();

        client.DefaultRequestHeaders.GetValues("X-Trace").Should().ContainSingle().Which.Should().Be("1");
        (await FluentActions.Awaiting(() => client.GetAsync("http://metadata.internal/")).Should().ThrowAsync<Exception>())
            .Which.GetBaseException().Should().BeOfType<SsrfBlockedException>();
        await FluentActions.Awaiting(() => client.GetAsync("http://127.0.0.1:1/")).Should().ThrowAsync<HttpRequestException>(
            "host permitido mas porta fechada: o socket é descartado e a falha propaga");
    }
}
