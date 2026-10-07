using System.Net;
using Aedis.Exceptions;
using Aedis.Signing.Abstractions;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;

namespace Aedis.Signing.AwsKms;

/// <summary>
///     Traduz erros do SDK do KMS para as famílias do framework: transitório vira
///     <see cref="ServiceTemporarilyUnavailableException" /> (retry); chave inexistente, inválida ou sem
///     permissão viram <see cref="SigningException" /> (permanente). <c>AlreadyExistsException</c> é propagada
///     intacta: é a corrida de <c>CreateAlias</c> entre réplicas.
/// </summary>
public static class AwsKmsExceptionMapper
{
    /// <summary>Nome do serviço nas exceções transitórias.</summary>
    public const string ServiceName = "aws-kms";

    private static readonly HashSet<string> TransientErrorCodes = new(StringComparer.OrdinalIgnoreCase) {
        "ThrottlingException", "Throttling", "TooManyRequestsException", "RequestLimitExceeded",
        "InternalFailure", "InternalError", "ServiceUnavailable", "RequestTimeout", "RequestTimeoutException"
    };

    private static readonly HashSet<string> AccessDeniedErrorCodes = new(StringComparer.OrdinalIgnoreCase) {
        "AccessDeniedException", "AccessDenied", "UnauthorizedAccess", "UnrecognizedClientException",
        "InvalidClientTokenId", "ExpiredToken", "ExpiredTokenException", "InvalidSignatureException"
    };

    /// <summary>Executa <paramref name="operation" /> traduzindo qualquer erro do SDK.</summary>
    public static async Task<T> WrapAsync<T>(string keyId, Func<Task<T>> operation, CancellationToken cancellationToken) {
        try {
            return await operation();
        }
        catch (AlreadyExistsException) {
            throw;
        }
        catch (Exception ex) when (ex is not SigningException && ex is not ServiceTemporarilyUnavailableException) {
            throw Translate(keyId, ex, cancellationToken);
        }
    }

    /// <summary>Traduz uma exceção do SDK; devolve a própria quando não há tradução.</summary>
    public static Exception Translate(string keyId, Exception ex, CancellationToken cancellationToken = default) {
        switch (ex) {
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                return ex;
            case OperationCanceledException:
            case TimeoutException:
            case HttpRequestException:
                return Transient(keyId, "expirou ou não pôde ser alcançado", ex);
            case NotFoundException:
                return new SigningKeyNotFoundException(keyId, $"A chave KMS '{keyId}' não foi encontrada.", ex);
            case DisabledException:
            case KMSInvalidStateException:
            case InvalidKeyUsageException:
            case Amazon.KeyManagementService.Model.UnsupportedOperationException:
                return new SigningKeyInvalidStateException(keyId, $"A chave KMS '{keyId}' não pode assinar: {ex.Message}", ex);
            case KMSInternalException:
            case DependencyTimeoutException:
            case KeyUnavailableException:
                return Transient(keyId, "devolveu um erro transitório", ex);
            case AmazonKeyManagementServiceException kms when IsAccessDenied(kms):
                return new SigningAccessDeniedException(keyId,
                    $"Acesso negado à chave KMS '{keyId}' (verifique a política IAM da identidade e a key policy).", ex);
            case AmazonServiceException svc when IsTransient(svc):
                return Transient(keyId, $"devolveu {svc.ErrorCode ?? ((int)svc.StatusCode).ToString()}", ex);
            case AmazonServiceException svc when IsAccessDenied(svc):
                return new SigningAccessDeniedException(keyId,
                    $"Acesso negado à chave KMS '{keyId}' (verifique a política IAM da identidade e a key policy).", ex);
            case AmazonKeyManagementServiceException kms:
                return new SigningException($"O KMS rejeitou a operação em '{keyId}': {kms.ErrorCode ?? kms.Message}", ex, keyId);
            case AmazonClientException:
                return Transient(keyId, "não pôde inicializar o cliente ou alcançar o serviço", ex);
            default:
                return ex;
        }
    }

    private static ServiceTemporarilyUnavailableException Transient(string keyId, string what, Exception ex) =>
        new(ServiceName, $"AWS KMS {what} ao usar a chave '{keyId}'.", ex);

    private static bool IsTransient(AmazonServiceException ex) =>
        ex.Retryable is not null
        || (int)ex.StatusCode >= 500
        || ex.StatusCode == HttpStatusCode.RequestTimeout
        || ex.StatusCode == HttpStatusCode.TooManyRequests
        || (ex.ErrorCode is not null && TransientErrorCodes.Contains(ex.ErrorCode));

    private static bool IsAccessDenied(AmazonServiceException ex) =>
        ex.StatusCode == HttpStatusCode.Forbidden
        || ex.StatusCode == HttpStatusCode.Unauthorized
        || (ex.ErrorCode is not null && AccessDeniedErrorCodes.Contains(ex.ErrorCode));
}
