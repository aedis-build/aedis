namespace Aedis.Messaging.Abstractions;

public interface IMessageBrokerService
{
    Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default)
        where T : class, IMessage;

    Task SubscribeAsync<T>(string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage;

    Task SubscribeAsync<T>(string queue, string exchange, IEnumerable<string> routingKeys,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage
        => SubscribeAsync(queue, exchange, routingKeys.FirstOrDefault() ?? string.Empty,
                          handler, retryOptions, cancellationToken);
}
