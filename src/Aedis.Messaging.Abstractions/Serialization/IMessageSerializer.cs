namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>
///     Estratégia de serialização/desserialização de payloads de mensagem, isolada por formato.
///     Cada implementação trata um content-type (JSON, MessagePack, texto, bytes); o
///     <see cref="MessageSerializerResolver" /> escolhe a estratégia adequada.
/// </summary>
public interface IMessageSerializer
{
    /// <summary>Content-type que esta estratégia produz e consome (ex.: "application/json").</summary>
    string ContentType { get; }

    /// <summary>Indica se esta estratégia sabe serializar o dado informado.</summary>
    bool CanSerialize(object data);

    /// <summary>Serializa o dado em bytes.</summary>
    ReadOnlyMemory<byte> Serialize(object data);

    /// <summary>Desserializa os bytes no tipo alvo.</summary>
    object? Deserialize(ReadOnlyMemory<byte> data, Type targetType);
}
