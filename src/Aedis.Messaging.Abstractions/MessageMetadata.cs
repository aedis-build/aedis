namespace Aedis.Messaging.Abstractions;

public sealed record MessageMetadata
{
    public MessageMetadata() { }

    public MessageMetadata(string exchange, string queue, string routingKey) {
        Exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
        Queue = queue ?? throw new ArgumentNullException(nameof(queue));
        RoutingKey = routingKey ?? throw new ArgumentNullException(nameof(routingKey));
    }

    public MessageMetadata(string exchange, string queue, IEnumerable<string> routingKeys) {
        Exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
        Queue = queue ?? throw new ArgumentNullException(nameof(queue));
        var list = (routingKeys ?? throw new ArgumentNullException(nameof(routingKeys))).ToList();
        if (list.Count == 0) throw new ArgumentException("At least one routing key is required.", nameof(routingKeys));
        RoutingKeys = list.AsReadOnly();
        RoutingKey = list[0];
    }

    public string Exchange { get; init; } = string.Empty;
    public string Queue { get; init; } = string.Empty;
    public string RoutingKey { get; init; } = string.Empty;
    public IReadOnlyList<string>? RoutingKeys { get; init; }

    public void Deconstruct(out string exchange, out string queue, out string routingKey) {
        exchange = Exchange;
        queue = Queue;
        routingKey = RoutingKey;
    }

    public override string ToString() {
        return RoutingKeys is { Count: > 1 }
            ? $"Exchange: {Exchange}, Queue: {Queue}, RoutingKeys: [{string.Join(", ", RoutingKeys)}]"
            : $"Exchange: {Exchange}, Queue: {Queue}, RoutingKey: {RoutingKey}";
    }
}
