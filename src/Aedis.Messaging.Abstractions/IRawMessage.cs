namespace Aedis.Messaging.Abstractions;

/// <summary>
/// Marca mensagens que chegam como bytes brutos no broker (ex.: payloads binários de
/// protocolos legados, como em filas IBM MQ). O framework chama FromRaw() em vez de
/// tentar deserializar JSON. Inverso simétrico de IMessage.ToData().
/// </summary>
public interface IRawMessage : IMessage
{
    void FromRaw(byte[] rawData, string correlationId = "");
}
