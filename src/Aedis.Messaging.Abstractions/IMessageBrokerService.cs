namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Fachada agnóstica de broker para publicar e assinar mensagens. Abstrai o transporte concreto
///     (ex.: RabbitMQ) atrás de operações tipadas de publish/subscribe, mais um publish de payload bruto
///     para interoperar com produtores externos. Injete esta interface no código de aplicação.
/// </summary>
public interface IMessageBrokerService
{
    /// <summary>
    ///     Publica uma mensagem tipada no exchange e routing key informados. O corpo é serializado pela
    ///     estratégia adequada ao payload de <see cref="IMessage.ToData" />.
    /// </summary>
    /// <typeparam name="T">Tipo da mensagem publicada.</typeparam>
    Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default)
        where T : class, IMessage;

    /// <summary>
    ///     Publica um payload bruto (bytes) com o content-type informado, sem envelope <see cref="IMessage" />.
    ///     Para interoperar com consumidores externos ou quando o conteúdo já está serializado.
    /// </summary>
    Task PublishRawAsync(string exchange, string routingKey, ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream", string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Assina uma fila ligada ao exchange por uma routing key, despachando cada mensagem ao
    ///     <paramref name="handler" />. A chamada permanece ativa mantendo o consumer vivo, aplicando a
    ///     política de <paramref name="retryOptions" /> (health-retry, backoff, dead-letter) às falhas.
    /// </summary>
    /// <typeparam name="T">Tipo da mensagem consumida.</typeparam>
    Task SubscribeAsync<T>(string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage;

    /// <summary>
    ///     Sobrecarga que aceita várias routing keys para a mesma fila. A implementação padrão assina pela
    ///     primeira chave informada.
    /// </summary>
    /// <typeparam name="T">Tipo da mensagem consumida.</typeparam>
    Task SubscribeAsync<T>(string queue, string exchange, IEnumerable<string> routingKeys,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage
        => SubscribeAsync(queue, exchange, routingKeys.FirstOrDefault() ?? string.Empty,
                          handler, retryOptions, cancellationToken);
}
