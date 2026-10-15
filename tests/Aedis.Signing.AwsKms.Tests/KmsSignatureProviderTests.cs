using System.Net;
using System.Security.Cryptography;
using System.Text;
using Aedis.Exceptions;
using Aedis.Signing.Abstractions;
using Aedis.Signing.AwsKms;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Signing.AwsKms.Tests;

/// <summary>
///     Costura de assinatura no KMS (SDK substituído): manda só o digest com <c>ECDSA_SHA_256</c>, converte o
///     DER devolvido em <c>r||s</c> verificável, e traduz os erros do SDK para as famílias do framework.
/// </summary>
public sealed class KmsSignatureProviderTests : IDisposable
{
    private readonly SigningKeyHandle _handle;
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly IAmazonKeyManagementService _kms = Substitute.For<IAmazonKeyManagementService>();
    private readonly ISigningKeyProvider _provider = Substitute.For<ISigningKeyProvider>();
    private readonly KmsSignatureProvider _sut;

    public KmsSignatureProviderTests() {
        var parameters = _key.ExportParameters(false);
        _handle = new SigningKeyHandle("kid-1", "arn:aws:kms:sa-east-1:123456789012:key/kid-1", SigningOptions.Algorithm, parameters,
            JsonWebKeyDocument.FromEcPublicKey(parameters, "kid-1"));
        _provider.GetKeyAsync(Arg.Any<CancellationToken>()).Returns(_handle);
        _sut = new KmsSignatureProvider(_kms, _provider);
    }

    public void Dispose() => _key.Dispose();

    [Fact]
    public async Task Assina_o_digest_via_kms_e_devolve_r_s_verificavel() {
        _kms.SignAsync(Arg.Any<SignRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => {
                var req = ci.Arg<SignRequest>();
                var digest = req.Message.ToArray();
                return new SignResponse { Signature = new MemoryStream(_key.SignHash(digest, DSASignatureFormat.Rfc3279DerSequence)), KeyId = _handle.ProviderKeyReference };
            });
        var input = Encoding.UTF8.GetBytes("\"@method\": POST\n\"@signature-params\": (...)");
        var digest = SHA256.HashData(input);

        var signature = await _sut.SignDigestAsync(digest);

        signature.Should().HaveCount(64);
        _key.VerifyData(input, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation).Should().BeTrue();
        await _kms.Received(1).SignAsync(Arg.Is<SignRequest>(r =>
                r.KeyId == _handle.ProviderKeyReference
                && r.MessageType == MessageType.DIGEST
                && r.SigningAlgorithm == SigningAlgorithmSpec.ECDSA_SHA_256
                && r.Message.Length == 32),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Chave_desabilitada_vira_SigningKeyInvalidState() {
        _kms.SignAsync(Arg.Any<SignRequest>(), Arg.Any<CancellationToken>())
            .Returns<SignResponse>(_ => throw new DisabledException("disabled"));

        var act = () => _sut.SignDigestAsync(new byte[32]);

        (await act.Should().ThrowAsync<SigningKeyInvalidStateException>()).Which.KeyId.Should().Be("kid-1");
    }

    [Fact]
    public async Task Erro_interno_do_kms_e_transitorio() {
        _kms.SignAsync(Arg.Any<SignRequest>(), Arg.Any<CancellationToken>())
            .Returns<SignResponse>(_ => throw new KMSInternalException("boom"));

        var act = () => _sut.SignDigestAsync(new byte[32]);

        (await act.Should().ThrowAsync<ServiceTemporarilyUnavailableException>()).Which.ServiceName.Should().Be("aws-kms");
    }

    [Fact]
    public async Task Chave_inexistente_vira_SigningKeyNotFound() {
        _kms.SignAsync(Arg.Any<SignRequest>(), Arg.Any<CancellationToken>())
            .Returns<SignResponse>(_ => throw new NotFoundException("gone"));

        var act = () => _sut.SignDigestAsync(new byte[32]);

        await act.Should().ThrowAsync<SigningKeyNotFoundException>();
    }

    [Fact]
    public async Task Access_denied_e_permanente() {
        _kms.SignAsync(Arg.Any<SignRequest>(), Arg.Any<CancellationToken>())
            .Returns<SignResponse>(_ => throw new AmazonKeyManagementServiceException("denied", ErrorType.Sender, "AccessDeniedException", "rid", HttpStatusCode.BadRequest));

        var act = () => _sut.SignDigestAsync(new byte[32]);

        var ex = await act.Should().ThrowAsync<SigningAccessDeniedException>();
        ex.Which.ShouldRequeue.Should().BeFalse();
    }

    [Fact]
    public async Task Throttling_e_transitorio() {
        _kms.SignAsync(Arg.Any<SignRequest>(), Arg.Any<CancellationToken>())
            .Returns<SignResponse>(_ => throw new AmazonKeyManagementServiceException("slow down", ErrorType.Sender, "ThrottlingException", "rid", HttpStatusCode.BadRequest));

        var act = () => _sut.SignDigestAsync(new byte[32]);

        await act.Should().ThrowAsync<ServiceTemporarilyUnavailableException>();
    }
}
