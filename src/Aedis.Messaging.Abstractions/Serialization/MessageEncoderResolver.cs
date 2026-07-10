namespace Aedis.Messaging.Abstractions.Serialization;

/// <summary>
///     Seleciona a estratégia de <em>content-encoding</em>: no publish por política (comprime quando ligado
///     e o payload atinge o limiar), no consume pelo <c>Content-Encoding</c> recebido. Espelha o
///     <see cref="MessageSerializerResolver" />: pequenos payloads não são comprimidos (o overhead do gzip
///     não compensa), e um <c>Content-Encoding</c> ausente/<c>identity</c> passa direto.
/// </summary>
public sealed class MessageEncoderResolver
{
    private readonly bool _compressionEnabled;
    private readonly int _compressionThresholdBytes;
    private readonly IReadOnlyList<IMessageEncoder> _encoders;
    private readonly IMessageEncoder _identity;
    private readonly IMessageEncoder _preferred;

    /// <summary>
    ///     Cria o resolvedor a partir das estratégias e da política de compressão. <paramref name="preferredEncoding" />
    ///     é o encoder usado no publish quando a compressão se aplica (padrão <c>gzip</c>); se ausente da lista,
    ///     cai em <c>identity</c>.
    /// </summary>
    public MessageEncoderResolver(IEnumerable<IMessageEncoder> encoders, bool compressionEnabled = true,
        int compressionThresholdBytes = 1024, string preferredEncoding = "gzip") {
        _encoders = encoders.ToList();
        _identity = _encoders.FirstOrDefault(e => e.Encoding == "identity") ?? new IdentityMessageEncoder();
        _preferred = _encoders.FirstOrDefault(e =>
            string.Equals(e.Encoding, preferredEncoding, StringComparison.OrdinalIgnoreCase)) ?? _identity;
        _compressionEnabled = compressionEnabled;
        _compressionThresholdBytes = compressionThresholdBytes;
    }

    /// <summary>Conjunto padrão: <c>identity</c> + <c>gzip</c>, compressão ligada a partir de 1 KB.</summary>
    public static MessageEncoderResolver CreateDefault() =>
        new([new IdentityMessageEncoder(), new GzipMessageEncoder()]);

    /// <summary>
    ///     Escolhe o encoder para o publish: o preferido quando a compressão está ligada e o payload
    ///     (<paramref name="payloadLength" />) atinge o limiar; caso contrário, <c>identity</c>.
    /// </summary>
    public IMessageEncoder ResolveForEncode(int payloadLength) =>
        _compressionEnabled && payloadLength >= _compressionThresholdBytes ? _preferred : _identity;

    /// <summary>
    ///     Escolhe o encoder para o consume conforme o <c>Content-Encoding</c> recebido. Ausente/vazio/<c>identity</c>
    ///     → sem transformação. Um encoding conhecido mas não registrado lança <see cref="NotSupportedException" />
    ///     (a mensagem não é processada e vai para a DLQ, em vez de ser corrompida silenciosamente).
    /// </summary>
    public IMessageEncoder ResolveForContentEncoding(string? contentEncoding) {
        if (string.IsNullOrEmpty(contentEncoding)
            || string.Equals(contentEncoding, "identity", StringComparison.OrdinalIgnoreCase))
            return _identity;

        foreach (var encoder in _encoders)
            if (string.Equals(encoder.Encoding, contentEncoding, StringComparison.OrdinalIgnoreCase))
                return encoder;

        throw new NotSupportedException($"Content-Encoding não suportado: '{contentEncoding}'.");
    }
}
