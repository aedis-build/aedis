namespace Aedis.Messaging.Abstractions;

/// <summary>
/// Mensagem que deseja receber os metadados do MQMD após o consumer IBM MQ lê-la.
/// Segue o mesmo padrão de <see cref="IRawMessage.FromRaw"/>.
/// Útil para mensagens de report do IBM MQ (ex.: confirmações COA/COD).
/// </summary>
public interface IMqMetadataMessage : IRawMessage
{
    /// <summary>
    ///     Recebe os metadados do MQMD lidos pelo consumer IBM MQ após a leitura da mensagem, permitindo
    ///     reagir a report codes (COA/COD/Expiry) e demais campos do cabeçalho.
    /// </summary>
    void FromMqMetadata(MqMessageMetadata metadata);
}
