using Aedis.Messaging.Abstractions;

namespace Aedis.App1.Worker.Messaging;

/// <summary>
///     Evento consumido pelo worker: pede o envio de uma notificação. A chave <see cref="Code" /> dá
///     idempotência ao processamento (reentregas do broker não duplicam o efeito).
/// </summary>
public sealed record NotificationRequestedEvent : IMessage {
    /// <summary>Nome do evento (routing key lógica).</summary>
    public const string Name = "notifications.notification.requested";

    /// <summary>Chave de negócio da notificação (idempotência).</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Destinatário.</summary>
    public string Recipient { get; init; } = string.Empty;

    /// <summary>Conteúdo.</summary>
    public string Content { get; init; } = string.Empty;

    /// <inheritdoc />
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();

    /// <inheritdoc />
    public DateTimeOffset Date { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public string EventName => Name;

    /// <inheritdoc />
    public object ToData() => this;
}
