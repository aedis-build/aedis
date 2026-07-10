namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>
///     Estratégia de <em>content-encoding</em> aplicada ao payload já serializado, antes do transporte —
///     tipicamente compressão. Complementa o <see cref="IMessageSerializer" /> (que decide o formato): o
///     encoder decide a codificação (ex.: <c>gzip</c>) e é sinalizado ao consumidor pelo cabeçalho/atributo
///     <c>Content-Encoding</c>, para que ele reverta a transformação antes de desserializar. Objetos grandes
///     porém compressíveis (JSON) trafegam com muito menos bytes.
/// </summary>
public interface IMessageEncoder
{
    /// <summary>Token de content-encoding que esta estratégia produz e consome (ex.: <c>gzip</c>, <c>identity</c>).</summary>
    string Encoding { get; }

    /// <summary>Codifica (ex.: comprime) o payload serializado para transporte.</summary>
    ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> data);

    /// <summary>Reverte a codificação, devolvendo o payload serializado original.</summary>
    ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> data);
}
