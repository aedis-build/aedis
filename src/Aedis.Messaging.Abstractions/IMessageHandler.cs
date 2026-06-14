namespace Aedis.Messaging.Abstractions;

public interface IMessageHandler
{
    Type MessageType { get; }
    Task HandleAsync(object message, CancellationToken cancellationToken);
}

public interface IMessageHandler<in T> where T : class, IMessage
{
    Task HandleAsync(T message, CancellationToken cancellationToken);
}