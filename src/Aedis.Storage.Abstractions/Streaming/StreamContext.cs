namespace Aedis.Storage.Abstractions.Streaming;

/// <summary>
///     Contexto agnóstico de provider para o processamento de stream de um objeto.
///     O provider preenche <see cref="SourceStream" /> (o stream bruto do objeto) e
///     <see cref="ContentLength" />; a estratégia correspondente ao <see cref="StreamMode" />
///     produz <see cref="Result" /> com o tratamento de memória adequado.
/// </summary>
public sealed class StreamContext
{
    public StreamMode Mode { get; set; }

    /// <summary>Stream bruto do objeto, fornecido pelo provider (ex.: corpo da resposta do download).</summary>
    public Stream SourceStream { get; set; } = null!;

    public long ContentLength { get; set; }

    /// <summary>Stream resultante após a estratégia de tratamento de memória.</summary>
    public Stream? Result { get; set; }
}
