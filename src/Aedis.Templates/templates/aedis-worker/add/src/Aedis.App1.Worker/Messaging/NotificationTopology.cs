namespace Aedis.App1.Worker.Messaging;

/// <summary>
///     Topologia de mensageria: exchange/tópico, fila e routing keys, compartilhadas entre o consumidor e o
///     publicador.
/// </summary>
public static class NotificationTopology {
    /// <summary>Exchange/tópico onde os eventos de notificação trafegam.</summary>
    public const string Exchange = "notifications-events";

    /// <summary>Fila consumida pelo worker para os pedidos de notificação.</summary>
    public const string RequestedQueue = "notifications.requested";

    /// <summary>Routing key dos pedidos de notificação (consumidos).</summary>
    public const string RequestedRoutingKey = NotificationRequestedEvent.Name;

    /// <summary>Routing key das notificações já processadas (publicadas).</summary>
    public const string SentRoutingKey = NotificationSentEvent.Name;
}
