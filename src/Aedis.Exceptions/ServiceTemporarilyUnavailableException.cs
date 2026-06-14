namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando um serviço está temporariamente indisponível.
///     Faz com que a mensagem aguarde na fila de health retry sem incrementar death count.
/// </summary>
public class ServiceTemporarilyUnavailableException : RetryableException
{
    public ServiceTemporarilyUnavailableException(string serviceName, string message)
        : base(message) {
        ServiceName = serviceName;
    }

    public ServiceTemporarilyUnavailableException(string serviceName, string message, Exception innerException)
        : base(message, innerException) {
        ServiceName = serviceName;
    }

    public string ServiceName { get; }
}