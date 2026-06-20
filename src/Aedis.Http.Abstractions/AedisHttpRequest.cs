namespace Aedis.Http.Abstractions;

/// <summary>
///     Requisição HTTP agnóstica de provider: método, URL (absoluta ou relativa ao <c>BaseAddress</c> do
///     cliente), cabeçalhos, corpo opcional e timeout opcional. Construída pelo código de integração e
///     enviada por um <see cref="IAedisHttpClient" />.
/// </summary>
public sealed class AedisHttpRequest
{
    /// <summary>Método HTTP. Default <c>GET</c>.</summary>
    public HttpMethod Method { get; init; } = HttpMethod.Get;

    /// <summary>URL alvo — absoluta, ou relativa ao <c>BaseAddress</c> do cliente quando este o define.</summary>
    public required string Url { get; init; }

    /// <summary>Cabeçalhos da requisição (ex.: <c>Authorization</c>, headers de roteamento).</summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Corpo da requisição, ou <c>null</c> para requisições sem corpo.</summary>
    public AedisHttpContent? Content { get; init; }

    /// <summary>Timeout específico desta requisição; quando <c>null</c>, usa o timeout do cliente.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Cria uma requisição <c>GET</c> para a <paramref name="url" />.</summary>
    public static AedisHttpRequest Get(string url) => new() { Method = HttpMethod.Get, Url = url };

    /// <summary>Cria uma requisição <c>POST</c> para a <paramref name="url" /> com o <paramref name="content" /> indicado.</summary>
    public static AedisHttpRequest Post(string url, AedisHttpContent? content = null) =>
        new() { Method = HttpMethod.Post, Url = url, Content = content };
}
