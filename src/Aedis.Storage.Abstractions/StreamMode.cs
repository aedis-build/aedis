namespace Aedis.Storage.Abstractions;

/// <summary>
///     Define como o conteúdo de um objeto baixado é tratado em memória ao ser lido. Escolha conforme
///     o tamanho esperado e o padrão de consumo: bufferizar tudo em memória, fazer spool para arquivo
///     temporário ou repassar o stream do provider sem copiar.
/// </summary>
public enum StreamMode : byte
{
    /// <summary>Sem bufferização: repassa o stream do provider diretamente (equivale a <see cref="Chunked" />).</summary>
    Default = 0,

    /// <summary>Carrega o objeto inteiro em <see cref="System.IO.MemoryStream" />; ideal para payloads pequenos.</summary>
    Memory = 1,

    /// <summary>Faz spool para arquivo temporário (deletado ao fechar), evitando pressão de memória em objetos grandes.</summary>
    TempFile = 2,

    /// <summary>Repassa o stream do provider sem copiar, para consumo progressivo de grandes downloads.</summary>
    Chunked = 3
}
