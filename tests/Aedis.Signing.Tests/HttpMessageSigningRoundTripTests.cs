using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Aedis.Signing.Abstractions;
using Aedis.Signing.Verification;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Aedis.Signing.Tests;

/// <summary>
///     Assina com o provider em processo (a mesma costura que um HSM usa, só muda quem faz o ECDSA) e verifica
///     com o verificador público: o que o receptor faz, feito aqui contra o que o emissor manda — inclusive
///     cada motivo de rejeição.
/// </summary>
public sealed class HttpMessageSigningRoundTripTests : IAsyncLifetime
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"id\":\"n-1\",\"type\":\"order.created\"}");
    private readonly ECDsa _key = TestSigningKeys.NewP256();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 7, 15, 0, 0, TimeSpan.Zero));
    private JsonWebKeySetDocument _jwks = null!;
    private HttpMessageSigner _signer = null!;

    public async Task InitializeAsync() {
        var (provider, state) = await TestSigningKeys.ReadyInProcessAsync(_key, "kid-1");
        var options = new SigningOptions { Http = { Tag = "loja-webhook" } };
        _signer = new HttpMessageSigner(provider, provider, options, state, NullLoggerFactory.Instance, _time);
        _jwks = new JsonWebKeySetDocument { Keys = [(await provider.GetKeyAsync()).Jwk] };
    }

    public Task DisposeAsync() {
        _key.Dispose();
        return Task.CompletedTask;
    }

    private static HttpRequestMessage Request(HttpContent? content = null, bool idempotencyKey = true) {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://partner.example.com/webhooks/loja?api_key=abc") {
            Content = content ?? new ByteArrayContent(Body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" } } }
        };
        if (idempotencyKey) request.Headers.Add("Idempotency-Key", "0191d9d4-6c3e-7a10-9f7e-2b1c0f6a1e42");
        return request;
    }

    private HttpMessageSignatureVerifier Verifier(string tag = "loja-webhook") =>
        new(new StaticJwksKeyResolver(_jwks), new HttpSignatureVerificationOptions { Tag = tag }, _time);

    [Fact]
    public async Task Assina_com_os_headers_e_parametros_da_rfc_9421() {
        var request = Request();

        await _signer.SignAsync(request);

        request.Headers.Date.Should().Be(_time.GetUtcNow());
        request.Headers.GetValues(ContentDigest.HeaderName).Single().Should().Be(ContentDigest.Compute(Body));

        var input = request.Headers.GetValues("Signature-Input").Single();
        input.Should().StartWith("sig1=(\"@method\" \"@target-uri\" \"content-type\" \"content-digest\" \"date\" \"idempotency-key\")");
        input.Should().Contain($"created={_time.GetUtcNow().ToUnixTimeSeconds()}");
        input.Should().Contain($"expires={_time.GetUtcNow().AddSeconds(300).ToUnixTimeSeconds()}");
        input.Should().Contain("keyid=\"kid-1\"").And.Contain("alg=\"ecdsa-p256-sha256\"").And.Contain("tag=\"loja-webhook\"");
        input.Should().NotContain("nonce");

        var signature = request.Headers.GetValues("Signature").Single();
        var match = Regex.Match(signature, "^sig1=:([A-Za-z0-9+/=]+):$");
        match.Success.Should().BeTrue(signature);
        Convert.FromBase64String(match.Groups[1].Value).Should().HaveCount(64, "ECDSA P-256 em r||s");
    }

    [Fact]
    public async Task Verificador_aceita_a_requisicao_assinada() {
        var request = Request();
        await _signer.SignAsync(request);

        var result = await Verifier().VerifyAsync(request);

        result.IsValid.Should().BeTrue(result.Detail);
        result.KeyId.Should().Be("kid-1");
        result.Created.Should().Be(_time.GetUtcNow());
        result.Expires.Should().Be(_time.GetUtcNow().AddSeconds(300));
    }

    [Fact]
    public async Task Componente_opcional_ausente_fica_fora_e_Date_existente_e_preservado() {
        var request = Request(idempotencyKey: false);
        var existingDate = _time.GetUtcNow().AddMinutes(-1);
        request.Headers.Date = existingDate;

        await _signer.SignAsync(request);

        request.Headers.Date.Should().Be(existingDate);
        request.Headers.GetValues("Signature-Input").Single().Should().NotContain("idempotency-key");
        (await Verifier().VerifyAsync(request)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Digest_e_dos_bytes_reais_de_StringContent() {
        var request = Request(new StringContent(Encoding.UTF8.GetString(Body), Encoding.UTF8, "application/json"));

        await _signer.SignAsync(request);

        request.Headers.GetValues(ContentDigest.HeaderName).Single().Should().Be(ContentDigest.Compute(Body));
        (await Verifier().VerifyAsync(request)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Overrides_por_chamada_trocam_tag_label_e_nonce() {
        var request = Request();

        await _signer.SignAsync(request, new HttpSigningOptions { Tag = "outro-uso", IncludeNonce = true, SignatureLabel = "sig2" });

        var input = request.Headers.GetValues("Signature-Input").Single();
        input.Should().StartWith("sig2=(").And.Contain("tag=\"outro-uso\"").And.Contain("nonce=\"");
        (await Verifier("outro-uso").VerifyAsync(request)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Corpo_alterado_e_DigestMismatch() {
        var request = Request();
        await _signer.SignAsync(request);
        var signed = await SignedHttpRequest.FromHttpRequestMessageAsync(request);
        var tampered = signed with { Body = Encoding.UTF8.GetBytes("{\"id\":\"n-1\",\"type\":\"order.cancelled\"}") };

        var result = await Verifier().VerifyAsync(tampered);

        result.IsValid.Should().BeFalse();
        result.Failure.Should().Be(HttpSignatureFailure.DigestMismatch);
    }

    [Fact]
    public async Task Header_coberto_alterado_e_SignatureMismatch() {
        var request = Request();
        await _signer.SignAsync(request);
        var signed = await SignedHttpRequest.FromHttpRequestMessageAsync(request);
        var tampered = signed with {
            Headers = signed.Headers.Select(h => h.Key.Equals("Idempotency-Key", StringComparison.OrdinalIgnoreCase)
                ? new KeyValuePair<string, string>(h.Key, "outra-chave") : h).ToList()
        };

        (await Verifier().VerifyAsync(tampered)).Failure.Should().Be(HttpSignatureFailure.SignatureMismatch);
    }

    [Fact]
    public async Task Url_diferente_da_assinada_e_SignatureMismatch() {
        var request = Request();
        await _signer.SignAsync(request);
        var signed = await SignedHttpRequest.FromHttpRequestMessageAsync(request);
        var tampered = signed with { TargetUri = new Uri("https://partner.example.com/webhooks/refund?api_key=abc") };

        (await Verifier().VerifyAsync(tampered)).Failure.Should().Be(HttpSignatureFailure.SignatureMismatch);
    }

    [Fact]
    public async Task Kid_desconhecido_e_UnknownKey() {
        var request = Request();
        await _signer.SignAsync(request);
        var verifier = new HttpMessageSignatureVerifier(new StaticJwksKeyResolver(new JsonWebKeySetDocument()),
            new HttpSignatureVerificationOptions { Tag = "loja-webhook" }, _time);

        (await verifier.VerifyAsync(request)).Failure.Should().Be(HttpSignatureFailure.UnknownKey);
    }

    [Fact]
    public async Task Expirada_e_Expired_e_created_no_futuro_e_CreatedInFuture() {
        var request = Request();
        await _signer.SignAsync(request);
        var signedAt = _time.GetUtcNow();

        _time.Advance(TimeSpan.FromSeconds(301));
        (await Verifier().VerifyAsync(request)).Failure.Should().Be(HttpSignatureFailure.Expired);

        var behind = new HttpMessageSignatureVerifier(new StaticJwksKeyResolver(_jwks),
            new HttpSignatureVerificationOptions { Tag = "loja-webhook" }, new FakeTimeProvider(signedAt.AddMinutes(-20)));
        (await behind.VerifyAsync(request)).Failure.Should().Be(HttpSignatureFailure.CreatedInFuture);
    }

    [Fact]
    public async Task Tag_diferente_ou_sem_assinatura_e_MissingSignature() {
        var request = Request();
        await _signer.SignAsync(request);
        (await Verifier("outro-uso").VerifyAsync(request)).Failure.Should().Be(HttpSignatureFailure.MissingSignature);

        var unsigned = Request();
        unsigned.Headers.TryAddWithoutValidation(ContentDigest.HeaderName, ContentDigest.Compute(Body));
        (await Verifier().VerifyAsync(unsigned)).Failure.Should().Be(HttpSignatureFailure.MissingSignature);
    }

    [Fact]
    public async Task Alg_diferente_e_WrongAlgorithm() {
        var request = Request();
        await _signer.SignAsync(request);
        var input = request.Headers.GetValues("Signature-Input").Single().Replace("ecdsa-p256-sha256", "rsa-v1_5-sha256");
        request.Headers.Remove("Signature-Input");
        request.Headers.TryAddWithoutValidation("Signature-Input", input);

        (await Verifier().VerifyAsync(request)).Failure.Should().Be(HttpSignatureFailure.WrongAlgorithm);
    }

    [Fact]
    public async Task Sem_chave_resolvida_no_prazo_lanca_SigningException() {
        var state = new SigningKeyState();
        var provider = new InProcessSigningKeyProvider(new SigningOptions { InProcess = { AllowEphemeralKey = true } }, state,
            NullLogger<InProcessSigningKeyProvider>.Instance);
        var signer = new HttpMessageSigner(provider, provider, new SigningOptions { BootstrapTimeoutSeconds = 5 }, state,
            NullLoggerFactory.Instance, _time);

        var signing = signer.SignAsync(Request());
        _time.Advance(TimeSpan.FromSeconds(6));

        var act = () => signing;
        await act.Should().ThrowAsync<SigningException>();
    }

    [Fact]
    public async Task Signer_desligado_nao_toca_a_requisicao() {
        var request = Request();

        await DisabledHttpMessageSigner.Instance.SignAsync(request);

        DisabledHttpMessageSigner.Instance.IsEnabled.Should().BeFalse();
        request.Headers.Contains("Signature").Should().BeFalse();
    }
}
