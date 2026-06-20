using System.Text;
using System.Text.Json;

namespace Aedis.Http.Abstractions;

/// <summary>
///     Resposta HTTP materializada e agnóstica de provider: status, cabeçalhos e o corpo já lido em bytes.
///     Materializar o corpo evita vazar streams/handles do provider concreto e simplifica o consumo nos
///     fluxos típicos (tokens, payloads de API). Use os auxiliares para ler como texto ou desserializar JSON.
/// </summary>
public sealed class AedisHttpResponse
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>Código de status HTTP da resposta.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Cabeçalhos da resposta.</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>Corpo da resposta já lido integralmente em bytes (pode ser vazio).</summary>
    public required byte[] Body { get; init; }

    /// <summary>Indica se o status está na faixa 2xx (sucesso).</summary>
    public bool IsSuccessStatusCode => StatusCode is >= 200 and < 300;

    /// <summary>Lê o corpo como texto UTF-8.</summary>
    public string ReadAsString() => Encoding.UTF8.GetString(Body);

    /// <summary>
    ///     Desserializa o corpo como JSON em <typeparamref name="T" /> (convenções web: camelCase,
    ///     case-insensitive). Devolve <c>default</c> quando o corpo está vazio.
    /// </summary>
    public T? ReadFromJson<T>(JsonSerializerOptions? options = null) =>
        Body.Length == 0 ? default : JsonSerializer.Deserialize<T>(Body, options ?? WebJson);
}
