namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Ponto de extensão para mensagens que querem receber os metadados NATIVOS do broker após o consumo,
///     sem acoplar esta camada de contratos a nenhum provider. O tipo <typeparamref name="TMetadata" /> é
///     definido pelo pacote do broker (que o liga aos campos nativos do cabeçalho) e é opaco para o framework:
///     o consumer do provider invoca <see cref="FromProviderMetadata" /> logo após reconstruir o payload
///     bruto, de modo que toda a tradução de cabeçalhos/confirmações específicos vive dentro do provider.
/// </summary>
/// <typeparam name="TMetadata">
///     Tipo de metadados nativos definido no pacote do provider; o framework apenas o repassa, sem conhecê-lo.
/// </typeparam>
public interface IProviderMetadataMessage<in TMetadata> : IRawMessage
{
    /// <summary>
    ///     Recebe os metadados nativos lidos pelo consumer do broker, permitindo à mensagem reagir a campos
    ///     de cabeçalho ou confirmação próprios do provider. Invocado após <see cref="IRawMessage.FromRaw" />.
    /// </summary>
    /// <param name="metadata">Metadados nativos do broker, no formato definido pelo provider.</param>
    void FromProviderMetadata(TMetadata metadata);
}
