namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando o processamento de uma mensagem excede o timeout configurado.
///     Herda de RetryableException para permitir retry com delay.
/// </summary>
public class MessageProcessingTimeoutException : RetryableException
{
    /// <summary>Cria a exceção usando o <paramref name="timeoutSeconds" /> também como atraso sugerido de retry.</summary>
    public MessageProcessingTimeoutException(string operation, int timeoutSeconds, string message)
        : base(message, TimeSpan.FromSeconds(timeoutSeconds)) {
        Operation = operation;
        TimeoutSeconds = timeoutSeconds;
    }

    /// <summary>Cria a exceção encadeando a causa original (<paramref name="innerException" />), ex.: a <see cref="TaskCanceledException" /> do timeout.</summary>
    public MessageProcessingTimeoutException(string operation, int timeoutSeconds, string message,
        Exception innerException)
        : base(message, innerException) {
        Operation = operation;
        TimeoutSeconds = timeoutSeconds;
    }

    /// <summary>Nome da operação que excedeu o tempo limite.</summary>
    public string Operation { get; }

    /// <summary>Tempo limite configurado, em segundos.</summary>
    public int TimeoutSeconds { get; }
}