namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando o processamento de uma mensagem excede o timeout configurado.
///     Herda de RetryableException para permitir retry com delay.
/// </summary>
public class MessageProcessingTimeoutException : RetryableException
{
    public MessageProcessingTimeoutException(string operation, int timeoutSeconds, string message)
        : base(message, TimeSpan.FromSeconds(timeoutSeconds)) {
        Operation = operation;
        TimeoutSeconds = timeoutSeconds;
    }

    public MessageProcessingTimeoutException(string operation, int timeoutSeconds, string message,
        Exception innerException)
        : base(message, innerException) {
        Operation = operation;
        TimeoutSeconds = timeoutSeconds;
    }

    public string Operation { get; }
    public int TimeoutSeconds { get; }
}