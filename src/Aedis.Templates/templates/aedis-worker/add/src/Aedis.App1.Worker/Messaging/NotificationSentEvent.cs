using Aedis.Messaging.Abstractions;

namespace Aedis.App1.Worker.Messaging;

/// <summary>
///     Evento de follow-up publicado pelo worker após processar uma notificação. Outros serviços podem reagir
///     a ele (encadeamento de processos).
/// </summary>
public sealed record NotificationSentEvent : IMessage {
    /// <summary>Nome do evento (routing key lógica).</summary>
    public const string Name = "notifications.notification.sent";

    /// <summary>Chave de negócio da notificação processada.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Destinatário.</summary>
    public string Recipient { get; init; } = string.Empty;

    /// <inheritdoc />
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();

    /// <inheritdoc />
    public DateTimeOffset Date { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public string EventName => Name;

    /// <inheritdoc />
    public object ToData() => this;
}
