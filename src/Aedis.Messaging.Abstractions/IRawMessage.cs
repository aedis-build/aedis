namespace Aedis.Messaging.Abstractions;

/// <summary>
/// Marca mensagens que chegam como bytes brutos no broker (ex.: payloads binários de
/// protocolos legados ou formatos proprietários). O framework chama FromRaw() em vez de
/// tentar deserializar JSON. Inverso simétrico de IMessage.ToData().
/// </summary>
public interface IRawMessage : IMessage
{
    /// <summary>
    ///     Reconstrói a mensagem a partir do payload bruto recebido do broker (e da correlação, quando houver),
    ///     em vez de desserializar JSON. Inverso simétrico de <see cref="IMessage.ToData" />.
    /// </summary>
    void FromRaw(byte[] rawData, string correlationId = "");
}
