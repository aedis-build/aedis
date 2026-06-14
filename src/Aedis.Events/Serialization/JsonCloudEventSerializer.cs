using System.Text;
using System.Text.Json;
using Aedis.Core.Utils;
using SystemJsonSerializer = System.Text.Json.JsonSerializer;
using Aedis.Events.Serialization.Abstractions;

namespace Aedis.Events.Serialization;

/// <summary>
///     Implementação de ICloudEventSerializer usando System.Text.Json.
///     Usa opções de serialização padrão do framework (CamelCase, WhenWritingNull).
/// </summary>
public class JsonCloudEventSerializer : ICloudEventSerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = SystemJsonOptionsFactory.Create();

    private readonly JsonSerializerOptions _options;

    /// <summary>
    ///     Cria uma instância de JsonCloudEventSerializer com opções padrão do framework.
    /// </summary>
    public JsonCloudEventSerializer() : this(DefaultOptions) { }

    /// <summary>
    ///     Cria uma instância de JsonCloudEventSerializer com opções customizadas.
    /// </summary>
    /// <param name="options">Opções de serialização JSON (opcional)</param>
    public JsonCloudEventSerializer(JsonSerializerOptions? options) {
        _options = options ?? DefaultOptions;
    }

    /// <inheritdoc />
    public string Serialize(ResourceCloudEvent cloudEvent) {
        if (cloudEvent == null)
            throw new ArgumentNullException(nameof(cloudEvent));

        return SystemJsonSerializer.Serialize(cloudEvent, _options);
    }

    /// <inheritdoc />
    public ResourceCloudEvent? Deserialize(string json) {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try {
            return SystemJsonSerializer.Deserialize<ResourceCloudEvent>(json, _options);
        }
        catch (JsonException) {
            return null;
        }
    }

    /// <inheritdoc />
    public byte[] SerializeToBytes(ResourceCloudEvent cloudEvent) {
        if (cloudEvent == null)
            throw new ArgumentNullException(nameof(cloudEvent));

        return Encoding.UTF8.GetBytes(Serialize(cloudEvent));
    }

    /// <inheritdoc />
    public ResourceCloudEvent? DeserializeFromBytes(byte[] bytes) {
        if (bytes == null || bytes.Length == 0)
            return null;

        try {
            var json = Encoding.UTF8.GetString(bytes);
            return Deserialize(json);
        }
        catch (ArgumentException) {
            return null;
        }
    }
}