namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>
///     Encoder no-op (<c>identity</c>): não transforma o payload. É o default quando a compressão está
///     desligada, quando o payload é pequeno demais para compensar, ou quando a mensagem chega sem
///     <c>Content-Encoding</c>.
/// </summary>
public sealed class IdentityMessageEncoder : IMessageEncoder
{
    /// <inheritdoc />
    public string Encoding => "identity";

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> data) => data;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> data) => data;
}
