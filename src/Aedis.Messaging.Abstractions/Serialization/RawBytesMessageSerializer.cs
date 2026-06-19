namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>Pass-through para payloads que já são <see cref="T:byte[]" /> (sem reserializar).</summary>
public sealed class RawBytesMessageSerializer : IMessageSerializer
{
    /// <inheritdoc />
    public string ContentType => "application/octet-stream";

    /// <inheritdoc />
    public bool CanSerialize(object data) => data is byte[];

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Serialize(object data) => (byte[])data;

    /// <inheritdoc />
    public object? Deserialize(ReadOnlyMemory<byte> data, Type targetType) => data.ToArray();
}
