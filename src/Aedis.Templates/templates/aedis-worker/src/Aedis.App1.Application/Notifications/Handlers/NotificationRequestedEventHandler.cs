using Aedis.App1.Application.Abstractions;
using Aedis.App1.Application.Notifications.Events;
using Aedis.App1.Domain.Entities;
using Aedis.Messaging.Abstractions;

namespace Aedis.App1.Application.Notifications.Handlers;

/// <summary>
///     Processa um <see cref="NotificationRequestedEvent" />: cria/recupera a notificação, marca como enviada,
///     persiste e publica o <see cref="NotificationSentEvent" />. É <strong>idempotente</strong> — se a
///     notificação já foi enviada (reentrega do broker), conclui sem efeito colateral. Concluir sem exceção
///     sinaliza ACK; lançar exceção aciona a política de retry/DLQ do consumidor.
/// </summary>
public sealed class NotificationRequestedEventHandler : IMessageHandler<NotificationRequestedEvent> {
    private readonly INotificationRepository _repository;
    private readonly IMessageBrokerService _broker;

    /// <summary>
    ///     Cria o handler com o repositório e o broker de mensagens.
    /// </summary>
    /// <param name="repository">Porta de saída do agregado.</param>
    /// <param name="broker">Broker para publicar o evento de follow-up.</param>
    public NotificationRequestedEventHandler(INotificationRepository repository, IMessageBrokerService broker) {
        _repository = repository;
        _broker = broker;
    }

    /// <inheritdoc />
    public async Task HandleAsync(NotificationRequestedEvent message, CancellationToken cancellationToken) {
        var notification = await _repository.GetByCodeAsync(message.Code, cancellationToken);
        if (notification is { Status: NotificationStatus.Sent }) {
            return;
        }

        notification ??= Notification.Request(message.Code, message.Recipient, message.Content);
        notification.MarkSent();
        await _repository.SaveAsync(notification, cancellationToken);

        await _broker.PublishAsync(
            NotificationTopology.Exchange,
            NotificationTopology.SentRoutingKey,
            new NotificationSentEvent {
                Code = notification.Code,
                Recipient = notification.Recipient,
                CorrelationId = message.CorrelationId
            },
            cancellationToken);
    }
}
