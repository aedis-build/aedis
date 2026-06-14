namespace Aedis.Messaging.Abstractions;

public interface IMessage
{
    string CorrelationId { get; }
    DateTimeOffset Date { get; }
    string EventName { get; }

    object ToData();
}