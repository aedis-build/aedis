namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Handler não genérico de mensagens, usado quando o tipo só é conhecido em runtime. Expõe o
///     <see cref="MessageType" /> esperado para o despacho dinâmico. Prefira <see cref="IMessageHandler{T}" />
///     no código de aplicação, que é fortemente tipado.
/// </summary>
public interface IMessageHandler
{
    /// <summary>Tipo concreto de mensagem que este handler processa, usado para roteamento dinâmico.</summary>
    Type MessageType { get; }

    /// <summary>Processa a mensagem recebida (já desserializada como <see cref="object" />).</summary>
    Task HandleAsync(object message, CancellationToken cancellationToken);
}

/// <summary>
///     Handler tipado de uma mensagem específica. Implemente para definir a lógica de negócio executada a
///     cada mensagem consumida; lance as exceções de retry/dead-letter do framework para controlar o destino
///     da mensagem em caso de falha. Registrado na assinatura da fila correspondente.
/// </summary>
/// <typeparam name="T">Tipo da mensagem consumida.</typeparam>
public interface IMessageHandler<in T> where T : class, IMessage
{
    /// <summary>Processa a mensagem tipada; concluir sem exceção sinaliza sucesso (ACK).</summary>
    Task HandleAsync(T message, CancellationToken cancellationToken);
}