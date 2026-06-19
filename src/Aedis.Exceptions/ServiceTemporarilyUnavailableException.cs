namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando um serviço está temporariamente indisponível.
///     Faz com que a mensagem aguarde na fila de health retry sem incrementar death count.
/// </summary>
public class ServiceTemporarilyUnavailableException : RetryableException
{
    /// <summary>Cria a exceção identificando o serviço temporariamente indisponível.</summary>
    public ServiceTemporarilyUnavailableException(string serviceName, string message)
        : base(message) {
        ServiceName = serviceName;
    }

    /// <summary>Cria a exceção encadeando a causa original (<paramref name="innerException" />).</summary>
    public ServiceTemporarilyUnavailableException(string serviceName, string message, Exception innerException)
        : base(message, innerException) {
        ServiceName = serviceName;
    }

    /// <summary>Nome do serviço que está temporariamente indisponível.</summary>
    public string ServiceName { get; }
}