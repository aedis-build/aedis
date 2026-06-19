using Aedis.Messaging.Abstractions;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Opt-in específico do IBM MQ: mensagens que querem receber os campos do MQMD após o consumo. Liga o
///     ponto de extensão genérico <see cref="IProviderMetadataMessage{TMetadata}" /> ao tipo nativo
///     <see cref="MqMessageMetadata" /> — implemente junto com a reconstrução bruta de
///     <see cref="IRawMessage" />. Útil para reagir a mensagens de report do IBM MQ (ex.: confirmações
///     COA/COD). O consumer preenche os metadados via <c>FromProviderMetadata</c> após <c>FromRaw</c>.
/// </summary>
public interface IMqMetadataMessage : IProviderMetadataMessage<MqMessageMetadata>
{
}
