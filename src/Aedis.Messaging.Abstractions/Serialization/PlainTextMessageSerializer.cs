using System.Text;

namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>Serialização de texto puro (UTF-8) para payloads que já são <see cref="string" />.</summary>
public sealed class PlainTextMessageSerializer : IMessageSerializer
{
    public string ContentType => "text/plain";

    public bool CanSerialize(object data) => data is string;

    public ReadOnlyMemory<byte> Serialize(object data) => Encoding.UTF8.GetBytes((string)data);

    public object? Deserialize(ReadOnlyMemory<byte> data, Type targetType) => Encoding.UTF8.GetString(data.Span);
}
