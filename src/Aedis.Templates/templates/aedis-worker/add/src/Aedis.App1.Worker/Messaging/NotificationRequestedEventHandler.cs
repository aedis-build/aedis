using Aedis.Messaging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aedis.App1.Worker.Messaging;

/// <summary>
///     Processa um <see cref="NotificationRequestedEvent" /> e publica o <see cref="NotificationSentEvent" />.
///     Versão autônoma (sem persistência) para ser adicionada a uma solução existente — concluir sem exceção
///     sinaliza ACK; lançar exceção aciona a política de retry/DLQ do consumidor.
/// </summary>
public sealed class NotificationRequestedEventHandler : IMessageHandler<NotificationRequestedEvent> {
    private readonly IMessageBrokerService _broker;
    private readonly ILogger<NotificationRequestedEventHandler> _logger;

    /// <summary>
    ///     Cria o handler com o broker e o logger.
    /// </summary>
    /// <param name="broker">Broker para publicar o evento de follow-up.</param>
    /// <param name="logger">Logger (a ofuscação de PII/segredos do Aedis se aplica automaticamente).</param>
    public NotificationRequestedEventHandler(IMessageBrokerService broker, ILogger<NotificationRequestedEventHandler> logger) {
        _broker = broker;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(NotificationRequestedEvent message, CancellationToken cancellationToken) {
        _logger.LogDebug("Processando notificação {Code} para {Recipient}.", message.Code, message.Recipient);

        // Para PERSISTIR de forma idempotente reusando suas camadas: adicione as ProjectReference no csproj,
        // injete seu repositório aqui e faça get-or-create + save antes de publicar (ver o template standalone).

        await _broker.PublishAsync(
            NotificationTopology.Exchange,
            NotificationTopology.SentRoutingKey,
            new NotificationSentEvent {
                Code = message.Code,
                Recipient = message.Recipient,
                CorrelationId = message.CorrelationId
            },
            cancellationToken);
    }
}
