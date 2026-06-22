using Aedis.App1.Application.Notifications;
using Aedis.App1.Application.Notifications.Events;
using Aedis.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aedis.App1.Worker.Messaging;

/// <summary>
///     Background service que assina a fila de pedidos de notificação e despacha cada mensagem ao handler
///     (em escopo próprio). A assinatura é resiliente: retry com backoff e dead-letter após o limite de
///     tentativas. <c>SubscribeAsync</c> roda até o shutdown gracioso cancelar o token.
/// </summary>
public sealed class NotificationConsumerService : BackgroundService {
    private readonly IMessageBrokerService _broker;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    ///     Cria o consumidor com o broker e a fábrica de escopos.
    /// </summary>
    /// <param name="broker">Broker de mensagens.</param>
    /// <param name="scopeFactory">Fábrica de escopos para o handler por mensagem.</param>
    public NotificationConsumerService(IMessageBrokerService broker, IServiceScopeFactory scopeFactory) {
        _broker = broker;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) {
        var handler = new ScopedMessageHandler<NotificationRequestedEvent>(_scopeFactory);

        var retry = new ConsumerRetryOptions {
            EnableRetryWithBackoff = true,
            EnableDeadLetter = true,
            MaxRetries = 5,
            BackoffDelaySeconds = 60
        };

        return _broker.SubscribeAsync(
            NotificationTopology.RequestedQueue,
            NotificationTopology.Exchange,
            NotificationTopology.RequestedRoutingKey,
            handler,
            retry,
            stoppingToken);
    }
}
