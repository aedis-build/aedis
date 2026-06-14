namespace Aedis.Events.Serialization.Abstractions;

/// <summary>
///     Interface para serialização e deserialização de ResourceCloudEvent.
/// </summary>
public interface ICloudEventSerializer
{
    /// <summary>
    ///     Serializa um ResourceCloudEvent para string.
    /// </summary>
    /// <param name="cloudEvent">Evento a ser serializado</param>
    /// <returns>String JSON do evento</returns>
    string Serialize(ResourceCloudEvent cloudEvent);

    /// <summary>
    ///     Deserializa uma string JSON para ResourceCloudEvent.
    /// </summary>
    /// <param name="json">String JSON a ser deserializada</param>
    /// <returns>ResourceCloudEvent deserializado</returns>
    ResourceCloudEvent? Deserialize(string json);

    /// <summary>
    ///     Serializa um ResourceCloudEvent para bytes.
    /// </summary>
    /// <param name="cloudEvent">Evento a ser serializado</param>
    /// <returns>Bytes UTF-8 do evento</returns>
    byte[] SerializeToBytes(ResourceCloudEvent cloudEvent);

    /// <summary>
    ///     Deserializa bytes UTF-8 para ResourceCloudEvent.
    /// </summary>
    /// <param name="bytes">Bytes UTF-8 a serem deserializados</param>
    /// <returns>ResourceCloudEvent deserializado</returns>
    ResourceCloudEvent? DeserializeFromBytes(byte[] bytes);
}