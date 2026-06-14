namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando há erro em chamada a serviço externo (HTTP API, etc).
///     Permite configurar se deve requeue baseado no status code.
/// </summary>
public class ExternalServiceException : Exception
{
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

    public string ServiceName { get; }
    public string? ErrorCode { get; }
    public int? StatusCode { get; }
    public string? ResponseBody { get; }
    public Dictionary<string, object> ErrorData { get; } = new();

    public bool ShouldRequeue => IsRetryableStatusCode();
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

    public void AddErrorData(string key, object value) {
        ErrorData[key] = value;
    }
}