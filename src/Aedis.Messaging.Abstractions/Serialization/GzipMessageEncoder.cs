using System.IO.Compression;

namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>
///     Encoder <c>gzip</c>: comprime o payload serializado. Sinalizado ao consumidor por
///     <c>Content-Encoding: gzip</c>. Para JSON de objetos grandes, tipicamente reduz o tamanho em várias
///     vezes — compensando com folga o overhead do base64 do transporte SQS/SNS.
/// </summary>
public sealed class GzipMessageEncoder : IMessageEncoder
{
    /// <inheritdoc />
    public string Encoding => "gzip";

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> data) {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
            gzip.Write(data.Span);
        return output.ToArray();
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> data) {
        using var input = new MemoryStream(data.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
