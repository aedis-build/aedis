using System.Diagnostics;

namespace Aedis.Messaging.Abstractions;

public abstract class MessageBase : IMessage
{
    public string CorrelationId { get; set; } = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
    public DateTimeOffset Date { get; set; } = DateTimeOffset.UtcNow;
    public abstract string EventName { get; }

    public virtual object ToData() {
        return this;
    }
}