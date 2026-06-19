namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando há erro em chamada a serviço externo (HTTP API, etc).
///     Permite configurar se deve requeue baseado no status code.
/// </summary>
public class ExternalServiceException : Exception
{
    /// <summary>Cria a exceção descrevendo a falha no serviço externo, com código de erro, status HTTP e corpo da resposta opcionais.</summary>
    public ExternalServiceException(
        string serviceName,
        string message,
        string? errorCode = null,
        int? statusCode = null,
        string? responseBody = null)
        : base(message) {
        ServiceName = serviceName;
        ErrorCode = errorCode;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>Cria a exceção encadeando a causa original (<paramref name="innerException" />), preservando o stack trace subjacente.</summary>
    public ExternalServiceException(
        string serviceName,
        string message,
        Exception innerException,
        string? errorCode = null,
        int? statusCode = null,
        string? responseBody = null)
        : base(message, innerException) {
        ServiceName = serviceName;
        ErrorCode = errorCode;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>Nome do serviço externo que falhou (ex.: "PaymentGateway").</summary>
    public string ServiceName { get; }

    /// <summary>Código de erro retornado pelo serviço, quando disponível.</summary>
    public string? ErrorCode { get; }

    /// <summary>Status HTTP da resposta, quando aplicável; orienta a decisão de requeue.</summary>
    public int? StatusCode { get; }

    /// <summary>Corpo bruto da resposta, preservado para diagnóstico.</summary>
    public string? ResponseBody { get; }

    /// <summary>Dados extras anexados ao erro para contexto e logging.</summary>
    public Dictionary<string, object> ErrorData { get; } = new();

    /// <summary>
    ///     Indica se a mensagem deve ser recolocada na fila. Verdadeiro quando não há status (falha de
    ///     conexão presumida transitória) ou quando o status é transitório (401, 408, 429 e qualquer 5xx).
    /// </summary>
    public bool ShouldRequeue => IsRetryableStatusCode();

    /// <summary>Indica se a mensagem deve ir para a dead-letter queue — o oposto de <see cref="ShouldRequeue" />.</summary>
    public bool SendToDeadLetterQueue => !ShouldRequeue;

    private bool IsRetryableStatusCode() {
        if (!StatusCode.HasValue)
            return true;

        return StatusCode.Value switch {
            401 => true,
            408 => true,
            429 => true,
            500 => true,
            502 => true,
            503 => true,
            504 => true,
            _ => StatusCode.Value >= 500
        };
    }

    /// <summary>Anexa um par chave/valor a <see cref="ErrorData" /> para enriquecer o contexto do erro.</summary>
    public void AddErrorData(string key, object value) {
        ErrorData[key] = value;
    }
}