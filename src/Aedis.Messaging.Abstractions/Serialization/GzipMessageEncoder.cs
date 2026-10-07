using System.IO.Compression;

namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>
///     Encoder <c>gzip</c>: comprime o payload serializado. Sinalizado ao consumidor por
///     <c>Content-Encoding: gzip</c>. Para JSON de objetos grandes, tipicamente reduz o tamanho em várias
///     vezes — compensando com folga o overhead do base64 do transporte SQS/SNS. A descompressão é limitada
///     a <see cref="MaxDecodedBytes" /> (padrão 4 MiB): um payload comprimido hostil não pode inflar a memória
///     do consumidor (decompression bomb) — ao exceder, a mensagem é rejeitada.
/// </summary>
public sealed class GzipMessageEncoder : IMessageEncoder
{
    /// <summary>Limite padrão do payload descomprimido: 4 MiB.</summary>
    public const int DefaultMaxDecodedBytes = 4 * 1024 * 1024;

    private const int ChunkSize = 80 * 1024;

    /// <summary>Cria o encoder com o limite de descompressão informado (mínimo 1 byte).</summary>
    public GzipMessageEncoder(int maxDecodedBytes = DefaultMaxDecodedBytes) {
        MaxDecodedBytes = Math.Max(1, maxDecodedBytes);
    }

    /// <summary>Tamanho máximo, em bytes, que <see cref="Decode" /> aceita produzir.</summary>
    public int MaxDecodedBytes { get; }

    /// <inheritdoc />
    public string Encoding => "gzip";

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> data) {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
            gzip.Write(data.Span);
        return output.ToArray();
    }

    /// <summary>
    ///     Descomprime em blocos, interrompendo assim que o total ultrapassa <see cref="MaxDecodedBytes" />.
    /// </summary>
    /// <exception cref="InvalidOperationException">O payload descomprimido excede <see cref="MaxDecodedBytes" />.</exception>
    public ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> data) {
        using var input = new MemoryStream(data.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        var buffer = new byte[ChunkSize];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0) {
            if (output.Length + read > MaxDecodedBytes)
                throw new InvalidOperationException(
                    $"Payload descomprimido excede o limite de {MaxDecodedBytes} bytes; mensagem rejeitada.");

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
