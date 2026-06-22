using Aedis.App1.Application.Abstractions;
using Aedis.App1.Application.Notifications;
using Aedis.App1.Application.Notifications.Events;
using Aedis.App1.Application.Notifications.Handlers;
using Aedis.App1.Domain.Entities;
using Aedis.Messaging.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.App1.UnitTests;

/// <summary>
///     Testa o handler de notificação: processa um pedido novo (persiste + publica o follow-up) e é
///     idempotente quando a notificação já foi enviada (reentrega do broker não duplica efeito).
/// </summary>
public sealed class NotificationRequestedEventHandlerTests {
    private readonly INotificationRepository _repository = Substitute.For<INotificationRepository>();
    private readonly IMessageBrokerService _broker = Substitute.For<IMessageBrokerService>();

    private NotificationRequestedEventHandler CreateHandler() => new(_repository, _broker);

    [Fact]
    public async Task Processa_pedido_novo_persiste_e_publica_follow_up() {
        _repository.GetByCodeAsync("N-1", Arg.Any<CancellationToken>()).Returns((Notification?)null);
        _repository.SaveAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<Notification>()));

        await CreateHandler().HandleAsync(
            new NotificationRequestedEvent { Code = "N-1", Recipient = "a@b.com", Content = "oi" },
            CancellationToken.None);

        await _repository.Received(1).SaveAsync(
            Arg.Is<Notification>(n => n.Code == "N-1" && n.Status == NotificationStatus.Sent),
            Arg.Any<CancellationToken>());
        await _broker.Received(1).PublishAsync(
            NotificationTopology.Exchange,
            NotificationTopology.SentRoutingKey,
            Arg.Any<NotificationSentEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignora_quando_ja_enviada_idempotencia() {
        var existing = Notification.Request("N-1", "a@b.com", "oi");
        existing.MarkSent();
        _repository.GetByCodeAsync("N-1", Arg.Any<CancellationToken>()).Returns(existing);

        await CreateHandler().HandleAsync(
            new NotificationRequestedEvent { Code = "N-1", Recipient = "a@b.com", Content = "oi" },
            CancellationToken.None);

        await _repository.DidNotReceive().SaveAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
        await _broker.DidNotReceive().PublishAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationSentEvent>(), Arg.Any<CancellationToken>());
    }
}
