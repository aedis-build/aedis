namespace Aedis.Messaging.Abstractions;

/// <summary>
/// Mensagem que deseja receber metadados do MQMD após deserialização pelo consumer IBM MQ.
/// Segue o mesmo padrão de <see cref="IRawMessage.FromRaw"/>.
/// Útil para mensagens de relatório (COA/COD) consumidas da fila QL.REP.
/// </summary>
public interface IMqMetadataMessage : IRawMessage
{
    void FromMqMetadata(MqMessageMetadata metadata);
}
