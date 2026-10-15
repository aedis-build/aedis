using System.Security.Cryptography;
using System.Text;
using Aedis.Exceptions;
using Aedis.Signing.Abstractions;
using Aedis.Signing.AwsKms;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Aedis.Signing.AwsKms.Tests;

/// <summary>
///     Resolução da chave no KMS (SDK substituído): KeyId explícito, alias existente, criação com alias e a
///     corrida entre réplicas; validação de estado/spec/uso; sondagem do health check.
/// </summary>
public sealed class KmsSigningKeyProviderTests : IDisposable
{
    private const string Alias = "alias/aedis/loja/signing";
    private const string Arn = "arn:aws:kms:sa-east-1:123456789012:key/11111111-1111-1111-1111-111111111111";
    private const string KeyId = "11111111-1111-1111-1111-111111111111";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly IAmazonKeyManagementService _kms = Substitute.For<IAmazonKeyManagementService>();
    private readonly SigningKeyState _state = new();

    public void Dispose() => _key.Dispose();

    private KmsSigningKeyProvider Build(AwsKmsSigningOptions? kmsOptions = null) =>
        new(_kms, new SigningOptions { ApplicationName = "loja" }, kmsOptions ?? new AwsKmsSigningOptions { KeyAlias = Alias },
            _state, NullLogger<KmsSigningKeyProvider>.Instance);

    private static KeyMetadata Metadata(string keyId = KeyId, string arn = Arn, KeyState? state = null) => new() {
        KeyId = keyId,
        Arn = arn,
        KeyState = state ?? KeyState.Enabled,
        KeySpec = KeySpec.ECC_NIST_P256,
        KeyUsage = KeyUsageType.SIGN_VERIFY,
        SigningAlgorithms = ["ECDSA_SHA_256"]
    };

    private void PublicKeyAvailable() =>
        _kms.GetPublicKeyAsync(Arg.Any<GetPublicKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetPublicKeyResponse { PublicKey = new MemoryStream(_key.ExportSubjectPublicKeyInfo()) });

    [Fact]
    public async Task KeyId_explicito_descreve_e_monta_handle_com_jwk_verificavel() {
        _kms.DescribeKeyAsync(Arg.Is<DescribeKeyRequest>(r => r.KeyId == KeyId), Arg.Any<CancellationToken>())
            .Returns(new DescribeKeyResponse { KeyMetadata = Metadata() });
        PublicKeyAvailable();

        var handle = await Build(new AwsKmsSigningOptions { KeyId = KeyId }).EnsureKeyAsync();

        handle.KeyId.Should().Be(KeyId);
        handle.ProviderKeyReference.Should().Be(Arn);
        handle.Algorithm.Should().Be("ecdsa-p256-sha256");
        _state.IsReady.Should().BeTrue();
        var data = Encoding.UTF8.GetBytes("x");
        var signature = _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        using var verifier = handle.Jwk.ToECDsa();
        verifier.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation).Should().BeTrue();
        await _kms.DidNotReceive().CreateKeyAsync(Arg.Any<CreateKeyRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Alias_inexistente_com_AutoCreate_cria_chave_etiquetada_e_alias() {
        var aliasCreated = false;
        _kms.DescribeKeyAsync(Arg.Any<DescribeKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => aliasCreated || ci.Arg<DescribeKeyRequest>().KeyId == KeyId
                ? Task.FromResult(new DescribeKeyResponse { KeyMetadata = Metadata() })
                : Task.FromException<DescribeKeyResponse>(new NotFoundException("alias não existe")));
        _kms.CreateKeyAsync(Arg.Any<CreateKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateKeyResponse { KeyMetadata = Metadata() });
        _kms.CreateAliasAsync(Arg.Any<CreateAliasRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => {
                aliasCreated = true;
                return Task.FromResult(new CreateAliasResponse());
            });
        PublicKeyAvailable();

        var handle = await Build(new AwsKmsSigningOptions { KeyAlias = Alias, AutoCreateKey = true, Tags = { ["team"] = "pagamentos" } }).EnsureKeyAsync();

        handle.KeyId.Should().Be(KeyId);
        await _kms.Received(1).CreateKeyAsync(Arg.Is<CreateKeyRequest>(r =>
            r.KeySpec == KeySpec.ECC_NIST_P256
            && r.KeyUsage == KeyUsageType.SIGN_VERIFY
            && r.Tags.Any(t => t.TagKey == "managed-by" && t.TagValue == "aedis")
            && r.Tags.Any(t => t.TagKey == "application" && t.TagValue == "loja")
            && r.Tags.Any(t => t.TagKey == "team" && t.TagValue == "pagamentos")), Arg.Any<CancellationToken>());
        await _kms.Received(1).CreateAliasAsync(Arg.Is<CreateAliasRequest>(r => r.AliasName == Alias && r.TargetKeyId == KeyId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Corrida_no_alias_usa_a_chave_vencedora_e_agenda_exclusao_da_orfa() {
        const string orphanId = "22222222-2222-2222-2222-222222222222";
        var aliasLost = false;
        _kms.DescribeKeyAsync(Arg.Any<DescribeKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => aliasLost
                ? Task.FromResult(new DescribeKeyResponse { KeyMetadata = Metadata() })
                : Task.FromException<DescribeKeyResponse>(new NotFoundException("ainda não")));
        _kms.CreateKeyAsync(Arg.Any<CreateKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateKeyResponse { KeyMetadata = Metadata(orphanId, "arn:orphan") });
        _kms.CreateAliasAsync(Arg.Any<CreateAliasRequest>(), Arg.Any<CancellationToken>())
            .Returns<CreateAliasResponse>(_ => {
                aliasLost = true;
                throw new AlreadyExistsException("outra réplica venceu");
            });
        _kms.ScheduleKeyDeletionAsync(Arg.Any<ScheduleKeyDeletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ScheduleKeyDeletionResponse());
        PublicKeyAvailable();

        var handle = await Build(new AwsKmsSigningOptions { KeyAlias = Alias, AutoCreateKey = true, OrphanKeyDeletionDays = 7 }).EnsureKeyAsync();

        handle.KeyId.Should().Be(KeyId, "a chave do alias vencedor é a usada");
        await _kms.Received(1).ScheduleKeyDeletionAsync(
            Arg.Is<ScheduleKeyDeletionRequest>(r => r.KeyId == orphanId && r.PendingWindowInDays == 7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Alias_inexistente_sem_AutoCreate_e_SigningKeyNotFound_com_orientacao() {
        _kms.DescribeKeyAsync(Arg.Any<DescribeKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns<DescribeKeyResponse>(_ => throw new NotFoundException("alias não existe"));

        var act = () => Build().EnsureKeyAsync();

        (await act.Should().ThrowAsync<SigningKeyNotFoundException>()).WithMessage("*AutoCreateKey*");
        await _kms.DidNotReceive().CreateKeyAsync(Arg.Any<CreateKeyRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Chave_desabilitada_e_InvalidState_e_Creating_e_transitoria() {
        _kms.DescribeKeyAsync(Arg.Any<DescribeKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DescribeKeyResponse { KeyMetadata = Metadata(state: KeyState.Disabled) });
        var disabled = () => Build().EnsureKeyAsync();
        await disabled.Should().ThrowAsync<SigningKeyInvalidStateException>();

        _kms.DescribeKeyAsync(Arg.Any<DescribeKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DescribeKeyResponse { KeyMetadata = Metadata(state: KeyState.Creating) });
        var creating = () => Build().EnsureKeyAsync();
        await creating.Should().ThrowAsync<ServiceTemporarilyUnavailableException>();
    }

    [Fact]
    public async Task Spec_ou_uso_errados_sao_InvalidState() {
        var metadata = Metadata();
        metadata.KeySpec = KeySpec.RSA_2048;
        _kms.DescribeKeyAsync(Arg.Any<DescribeKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DescribeKeyResponse { KeyMetadata = metadata });

        var act = () => Build().EnsureKeyAsync();

        (await act.Should().ThrowAsync<SigningKeyInvalidStateException>()).WithMessage("*ECC_NIST_P256*");
    }

    [Fact]
    public async Task Probe_reporta_o_estado_da_chave_sem_listar() {
        _kms.DescribeKeyAsync(Arg.Any<DescribeKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DescribeKeyResponse { KeyMetadata = Metadata(state: KeyState.PendingDeletion) });
        var handle = new SigningKeyHandle(KeyId, Arn, SigningOptions.Algorithm, _key.ExportParameters(false),
            JsonWebKeyDocument.FromEcPublicKey(_key.ExportParameters(false), KeyId));

        var result = await Build().ProbeAsync(handle);

        result.IsUsable.Should().BeFalse();
        result.Description.Should().Be("PendingDeletion");
        await _kms.Received(1).DescribeKeyAsync(Arg.Is<DescribeKeyRequest>(r => r.KeyId == Arn), Arg.Any<CancellationToken>());
    }
}
