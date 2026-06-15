namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>
///     Seleciona a estratégia de serialização adequada — por característica do dado (no publish) ou por
///     content-type (no consume). A ordem das estratégias define a prioridade no publish; o JSON é o fallback.
/// </summary>
public sealed class MessageSerializerResolver
{
    private readonly IMessageSerializer _fallback;
    private readonly IReadOnlyList<IMessageSerializer> _serializers;

    public MessageSerializerResolver(IEnumerable<IMessageSerializer> serializers) {
        _serializers = serializers.ToList();
        _fallback = _serializers.FirstOrDefault(s => s is JsonMessageSerializer) ?? new JsonMessageSerializer();
    }

    /// <summary>Conjunto padrão: bytes → texto → MessagePack → JSON (fallback).</summary>
    public static MessageSerializerResolver CreateDefault() {
        return new MessageSerializerResolver([
            new RawBytesMessageSerializer(),
            new PlainTextMessageSerializer(),
            new MessagePackMessageSerializer(),
            new JsonMessageSerializer()
        ]);
    }

    /// <summary>Escolhe a estratégia para serializar o dado (primeira que aceita; JSON como fallback).</summary>
    public IMessageSerializer ResolveForSerialize(object data) {
        foreach (var serializer in _serializers)
            if (serializer.CanSerialize(data))
                return serializer;

        return _fallback;
    }

    /// <summary>Escolhe a estratégia para desserializar conforme o content-type (JSON como fallback).</summary>
    public IMessageSerializer ResolveForContentType(string? contentType) {
        if (!string.IsNullOrEmpty(contentType))
            foreach (var serializer in _serializers)
                if (string.Equals(serializer.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
                    return serializer;

        return _fallback;
    }
}
