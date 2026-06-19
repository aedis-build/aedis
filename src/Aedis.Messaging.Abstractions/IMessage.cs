namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Contrato de uma mensagem de domínio que trafega pelo broker. Carrega correlação, data e o nome do
///     evento, e sabe expor seu payload serializável via <see cref="ToData" />. Implemente (em geral via
///     <see cref="MessageBase" />) para publicar e consumir mensagens tipadas.
/// </summary>
public interface IMessage
{
    /// <summary>Identificador de correlação que liga a mensagem ao trace/fluxo de origem.</summary>
    string CorrelationId { get; }

    /// <summary>Momento de criação da mensagem (UTC), usado para auditoria e ordenação lógica.</summary>
    DateTimeOffset Date { get; }

    /// <summary>Nome do evento que a mensagem representa (ex.: <c>"order.created"</c>); usado em roteamento e logs.</summary>
    string EventName { get; }

    /// <summary>
    ///     Retorna o objeto a ser serializado no corpo da mensagem. Por padrão é a própria instância; sobrescreva
    ///     para enviar um payload diferente do envelope (ex.: bytes brutos ou um DTO enxuto).
    /// </summary>
    object ToData();
}