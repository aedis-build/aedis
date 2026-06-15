using MessagePack;

namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>
///     Serialização binária MessagePack. Aplica-se a tipos anotados com <c>[MessagePackObject]</c>.
/// </summary>
public sealed class MessagePackMessageSerializer : IMessageSerializer
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard;

    public string ContentType => "application/x-msgpack";

    public bool CanSerialize(object data) {
        return data is not null && Attribute.IsDefined(data.GetType(), typeof(MessagePackObjectAttribute));
    }

    public ReadOnlyMemory<byte> Serialize(object data) {
        return MessagePackSerializer.Serialize(data.GetType(), data, Options);
    }

    public object? Deserialize(ReadOnlyMemory<byte> data, Type targetType) {
        return MessagePackSerializer.Deserialize(targetType, data, Options);
    }
}
