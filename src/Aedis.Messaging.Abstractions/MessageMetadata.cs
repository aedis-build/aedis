namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Descreve o endereçamento de uma mensagem no broker: exchange, fila e routing key (ou várias).
///     Use ao registrar uma assinatura ou para transportar a topologia de destino entre camadas.
///     Aceita uma única routing key ou um conjunto; quando há várias, <see cref="RoutingKey" /> guarda a primeira.
/// </summary>
public sealed record MessageMetadata
{
    /// <summary>Cria metadados vazios; preencha as propriedades via inicializador de objeto.</summary>
    public MessageMetadata() { }

    /// <summary>Cria metadados com uma única routing key. Os três argumentos são obrigatórios.</summary>
    public MessageMetadata(string exchange, string queue, string routingKey) {
        Exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
        Queue = queue ?? throw new ArgumentNullException(nameof(queue));
        RoutingKey = routingKey ?? throw new ArgumentNullException(nameof(routingKey));
    }

    /// <summary>
    ///     Cria metadados com várias routing keys (ex.: uma fila ligada a vários tópicos). Exige pelo menos
    ///     uma chave; a primeira também vira <see cref="RoutingKey" /> para compatibilidade com o caminho simples.
    /// </summary>
    public MessageMetadata(string exchange, string queue, IEnumerable<string> routingKeys) {
        Exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
        Queue = queue ?? throw new ArgumentNullException(nameof(queue));
        var list = (routingKeys ?? throw new ArgumentNullException(nameof(routingKeys))).ToList();
        if (list.Count == 0) throw new ArgumentException("At least one routing key is required.", nameof(routingKeys));
        RoutingKeys = list.AsReadOnly();
        RoutingKey = list[0];
    }

    /// <summary>Exchange de destino.</summary>
    public string Exchange { get; init; } = string.Empty;

    /// <summary>Fila de destino.</summary>
    public string Queue { get; init; } = string.Empty;

    /// <summary>Routing key principal; quando há várias, é a primeira de <see cref="RoutingKeys" />.</summary>
    public string RoutingKey { get; init; } = string.Empty;

    /// <summary>Conjunto de routing keys quando a assinatura cobre mais de um tópico; <c>null</c> no caso simples.</summary>
    public IReadOnlyList<string>? RoutingKeys { get; init; }

    /// <summary>Permite desestruturar os metadados em <c>(exchange, queue, routingKey)</c>.</summary>
    public void Deconstruct(out string exchange, out string queue, out string routingKey) {
        exchange = Exchange;
        queue = Queue;
        routingKey = RoutingKey;
    }

    /// <summary>Representação legível dos metadados, útil em logs (lista as routing keys quando há várias).</summary>
    public override string ToString() {
        return RoutingKeys is { Count: > 1 }
            ? $"Exchange: {Exchange}, Queue: {Queue}, RoutingKeys: [{string.Join(", ", RoutingKeys)}]"
            : $"Exchange: {Exchange}, Queue: {Queue}, RoutingKey: {RoutingKey}";
    }
}
