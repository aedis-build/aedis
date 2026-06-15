namespace Aedis.Messaging.Abstractions;

public interface IMessageBrokerService
{
    Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default)
        where T : class, IMessage;

    /// <summary>
    ///     Publica um payload bruto (bytes) com o content-type informado, sem envelope <see cref="IMessage" />.
    ///     Para interoperar com consumidores externos ou quando o conteúdo já está serializado.
    /// </summary>
    Task PublishRawAsync(string exchange, string routingKey, ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream", string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task SubscribeAsync<T>(string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage;

    Task SubscribeAsync<T>(string queue, string exchange, IEnumerable<string> routingKeys,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage
        => SubscribeAsync(queue, exchange, routingKeys.FirstOrDefault() ?? string.Empty,
                          handler, retryOptions, cancellationToken);
}
