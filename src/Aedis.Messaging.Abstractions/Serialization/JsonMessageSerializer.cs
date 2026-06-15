using System.Text.Json;
using Aedis.Core.Utils;

namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>Serialização JSON (System.Text.Json). É a estratégia padrão/fallback.</summary>
public sealed class JsonMessageSerializer : IMessageSerializer
{
    public string ContentType => "application/json";

    public bool CanSerialize(object data) => true;

    public ReadOnlyMemory<byte> Serialize(object data) {
        return JsonSerializer.SerializeToUtf8Bytes(data, SystemJsonOptionsFactory.Create());
    }

    public object? Deserialize(ReadOnlyMemory<byte> data, Type targetType) {
        return JsonSerializer.Deserialize(data.Span, targetType, SystemJsonOptionsFactory.Create());
    }
}
