using System.Net;
using Aedis.Exceptions;
using Aedis.Signing.Abstractions;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using FluentAssertions;
using Xunit;

namespace Aedis.Signing.AwsKms.Tests;

/// <summary>Tradução das falhas do SDK/KMS em exceções do Aedis: transitórias, chave ausente/inválida, acesso negado e genéricas.</summary>
public sealed class AwsKmsExceptionMapperTests
{
    private const string Key = "alias/aedis";

    private static AmazonServiceException Service(HttpStatusCode status, string? code = null) =>
        new("falha", ErrorType.Unknown, code, "req", status);

    private static AmazonKeyManagementServiceException Kms(HttpStatusCode status, string? code = null) =>
        new("falha", ErrorType.Unknown, code, "req", status);

    [Fact]
    public void Cancelamento_pedido_pelo_chamador_passa_direto() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new OperationCanceledException();

        AwsKmsExceptionMapper.Translate(Key, ex, cts.Token).Should().BeSameAs(ex);
    }

    [Fact]
    public void Timeouts_e_falhas_de_rede_sao_transitorias() {
        AwsKmsExceptionMapper.Translate(Key, new OperationCanceledException()).Should().BeOfType<ServiceTemporarilyUnavailableException>();
        AwsKmsExceptionMapper.Translate(Key, new TimeoutException()).Should().BeOfType<ServiceTemporarilyUnavailableException>();
        AwsKmsExceptionMapper.Translate(Key, new HttpRequestException()).Should().BeOfType<ServiceTemporarilyUnavailableException>();
        AwsKmsExceptionMapper.Translate(Key, new KMSInternalException("x")).Should().BeOfType<ServiceTemporarilyUnavailableException>();
        AwsKmsExceptionMapper.Translate(Key, new AmazonClientException("x")).Should().BeOfType<ServiceTemporarilyUnavailableException>();
        AwsKmsExceptionMapper.Translate(Key, Service(HttpStatusCode.ServiceUnavailable)).Should().BeOfType<ServiceTemporarilyUnavailableException>()
            .Which.ServiceName.Should().Be(AwsKmsExceptionMapper.ServiceName);
        AwsKmsExceptionMapper.Translate(Key, Service(HttpStatusCode.BadRequest, "ThrottlingException")).Should().BeOfType<ServiceTemporarilyUnavailableException>();
    }

    [Fact]
    public void Chave_ausente_ou_inutilizavel() {
        AwsKmsExceptionMapper.Translate(Key, new NotFoundException("x")).Should().BeOfType<SigningKeyNotFoundException>()
            .Which.Message.Should().Contain(Key);
        AwsKmsExceptionMapper.Translate(Key, new DisabledException("desabilitada")).Should().BeOfType<SigningKeyInvalidStateException>()
            .Which.Message.Should().Contain("desabilitada");
        AwsKmsExceptionMapper.Translate(Key, new InvalidKeyUsageException("uso")).Should().BeOfType<SigningKeyInvalidStateException>();
    }

    [Fact]
    public void Acesso_negado_por_status_ou_por_codigo_e_erro_generico_do_kms() {
        AwsKmsExceptionMapper.Translate(Key, Kms(HttpStatusCode.BadRequest, "AccessDeniedException")).Should().BeOfType<SigningAccessDeniedException>();
        AwsKmsExceptionMapper.Translate(Key, Service(HttpStatusCode.Forbidden)).Should().BeOfType<SigningAccessDeniedException>();
        AwsKmsExceptionMapper.Translate(Key, Kms(HttpStatusCode.BadRequest, "ValidationException")).Should().BeOfType<SigningException>()
            .Which.Message.Should().Contain("ValidationException");
        var unknown = new InvalidOperationException("outra");
        AwsKmsExceptionMapper.Translate(Key, unknown).Should().BeSameAs(unknown);
    }

    [Fact]
    public async Task WrapAsync_traduz_falhas_e_deixa_AlreadyExists_e_erros_de_assinatura_passarem() {
        var translated = () => AwsKmsExceptionMapper.WrapAsync<int>(Key, () => throw new NotFoundException("x"), CancellationToken.None);
        var passthrough = () => AwsKmsExceptionMapper.WrapAsync<int>(Key, () => throw new AlreadyExistsException("x"), CancellationToken.None);
        var signing = () => AwsKmsExceptionMapper.WrapAsync<int>(Key, () => throw new SigningKeyNotFoundException(Key, "x"), CancellationToken.None);

        await translated.Should().ThrowAsync<SigningKeyNotFoundException>();
        await passthrough.Should().ThrowAsync<AlreadyExistsException>();
        await signing.Should().ThrowAsync<SigningKeyNotFoundException>();
        (await AwsKmsExceptionMapper.WrapAsync(Key, () => Task.FromResult(7), CancellationToken.None)).Should().Be(7);
    }
}
