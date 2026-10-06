using Aedis.Messaging.Abstractions;

namespace Aedis.Messaging;

/// <summary>
///     Parâmetros de um <see cref="MessageConsumerService{TMessage}" />: a assinatura (fila, exchange e
///     routing keys), a política de retry do provider e os intervalos de reassinatura quando o broker cai.
/// </summary>
public sealed class MessageConsumerOptions
{
    /// <summary>Cria as opções para a assinatura <paramref name="subscription" />.</summary>
    public MessageConsumerOptions(MessageMetadata subscription) {
        Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
    }

    /// <summary>Endereçamento da assinatura: fila, exchange e routing key(s).</summary>
    public MessageMetadata Subscription { get; }

    /// <summary>Política de retry/dead-letter aplicada pelo provider às falhas do handler.</summary>
    public ConsumerRetryOptions Retry { get; init; } = new();

    /// <summary>Espera antes de reassinar após uma falha inesperada da assinatura. Padrão 30 s.</summary>
    public TimeSpan ResubscribeDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Espera antes de reassinar quando a assinatura termina por cancelamento que não veio do shutdown
    ///     (ex.: o provider cancelou o consumer internamente). Padrão 10 s.
    /// </summary>
    public TimeSpan CancellationRetryDelay { get; init; } = TimeSpan.FromSeconds(10);
}
