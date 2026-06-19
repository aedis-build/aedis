using System.Text.RegularExpressions;
using MessagePack;

namespace Aedis.Storage.Abstractions;

/// <summary>
///     Identifica um objeto dentro de um bucket/diretório: o bucket de origem, o caminho relativo e a
///     data da última modificação. Retornado pelas operações de listagem, é serializável via MessagePack
///     e <c>System.Text.Json</c> para trânsito entre serviços.
/// </summary>
/// <param name="BucketName">Nome do bucket/container/pasta de origem.</param>
/// <param name="FilePath">Caminho (chave) do objeto, relativo à raiz do bucket.</param>
/// <param name="LastModified">Momento da última modificação do objeto.</param>
[MessagePackObject]
[method: SerializationConstructor]
[method: System.Text.Json.Serialization.JsonConstructor]
public record BucketObject(
    [property: Key(0)] string BucketName,
    [property: Key(1)] string FilePath,
    [property: Key(2)] DateTimeOffset LastModified)
{
    /// <summary>
    ///     Cria o objeto carimbando <see cref="LastModified" /> com o instante atual (UTC). Útil ao
    ///     registrar um objeto recém-criado cuja data ainda não foi consultada no provider.
    /// </summary>
    public BucketObject(string bucketName, string filePath)
        : this(bucketName, filePath, DateTimeOffset.UtcNow) { }

    /// <summary>Nome do arquivo (último segmento de <see cref="FilePath" />), sem o caminho.</summary>
    [Key(3)] public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    ///     Representação <c>bucket/caminho</c> do objeto, com barras duplicadas colapsadas em uma só.
    /// </summary>
    public override string ToString() {
        return Regex.Replace($"{BucketName}/{FilePath}", "/{2,}", "/");
    }
}
