using MessagePack;

namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>
///     Serialização binária MessagePack. Aplica-se a tipos anotados com <c>[MessagePackObject]</c>.
/// </summary>
public sealed class MessagePackMessageSerializer : IMessageSerializer
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard;

    /// <inheritdoc />
    public string ContentType => "application/x-msgpack";

    /// <inheritdoc />
    public bool CanSerialize(object data) {
        return data is not null && Attribute.IsDefined(data.GetType(), typeof(MessagePackObjectAttribute));
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Serialize(object data) {
        return MessagePackSerializer.Serialize(data.GetType(), data, Options);
    }

    /// <inheritdoc />
    public object? Deserialize(ReadOnlyMemory<byte> data, Type targetType) {
        return MessagePackSerializer.Deserialize(targetType, data, Options);
    }
}
