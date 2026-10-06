using Aedis.Messaging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aedis.Messaging;

/// <summary>
///     Consumer hospedado genérico: mantém uma assinatura de <typeparamref name="TMessage" /> viva pelo tempo
///     de vida do host, despachando cada mensagem ao <see cref="IMessageHandler{T}" /> informado. Se a
///     assinatura terminar ou falhar sem que o host esteja encerrando, aguarda o intervalo configurado e
///     reassina — o consumer sobrevive a quedas e reinícios do broker. O cancelamento do shutdown encerra o
///     loop sem erro. Registrado por <c>AddAedisMessageConsumer</c>; herde apenas para customizar o loop.
/// </summary>
/// <typeparam name="TMessage">Tipo da mensagem consumida.</typeparam>
public class MessageConsumerService<TMessage> : BackgroundService where TMessage : class, IMessage
{
    private readonly IMessageBrokerService _broker;
    private readonly IMessageHandler<TMessage> _handler;
    private readonly ILogger<MessageConsumerService<TMessage>> _logger;
    private readonly MessageConsumerOptions _options;

    /// <summary>Cria o consumer sobre o broker, o handler (já adaptado para escopo, se preciso) e as opções.</summary>
    public MessageConsumerService(IMessageBrokerService broker, IMessageHandler<TMessage> handler,
        MessageConsumerOptions options, ILogger<MessageConsumerService<TMessage>> logger) {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var subscription = _options.Subscription;

        while (!stoppingToken.IsCancellationRequested) {
            TimeSpan delay;
            try {
                await SubscribeAsync(subscription, stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                return;
            }
            catch (OperationCanceledException exception) {
                delay = _options.CancellationRetryDelay;
                _logger.LogWarning(exception,
                    "Assinatura de {Message} em {Subscription} foi cancelada fora do shutdown; reassinando em {Delay}.",
                    typeof(TMessage).Name, subscription, delay);
            }
            catch (Exception exception) {
                delay = _options.ResubscribeDelay;
                _logger.LogError(exception,
                    "Assinatura de {Message} em {Subscription} falhou; reassinando em {Delay}.",
                    typeof(TMessage).Name, subscription, delay);
            }

            await WaitAsync(delay, stoppingToken);
        }
    }

    private Task SubscribeAsync(MessageMetadata subscription, CancellationToken stoppingToken) {
        return subscription.RoutingKeys is { Count: > 1 } routingKeys
            ? _broker.SubscribeAsync(subscription.Queue, subscription.Exchange, routingKeys, _handler, _options.Retry, stoppingToken)
            : _broker.SubscribeAsync(subscription.Queue, subscription.Exchange, subscription.RoutingKey, _handler, _options.Retry, stoppingToken);
    }

    private static async Task WaitAsync(TimeSpan delay, CancellationToken stoppingToken) {
        try {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        }
    }
}
