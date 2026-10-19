using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Aedis.Hypermedia.Abstractions;

/// <summary>Contrato dos recursos que anunciam ações autodescritivas em <c>_templates</c>.</summary>
public interface IHasTemplates
{
    /// <summary>Templates indexados pelo nome (normalmente o <c>rel</c> do link correspondente); <c>null</c> quando não há.</summary>
    IDictionary<string, HalFormsTemplate>? Templates { get; }
}

/// <summary>
///     Ação autodescritiva anunciada em <c>_templates</c>. Diferente do HAL-FORMS canônico, que descreve os
///     campos em <c>properties</c>, esta variante entrega em <see cref="Body" /> o próprio corpo pronto para
///     envio — o <c>rel</c> já codifica a intenção (ex.: <c>export-all-xlsx</c>, <c>next</c>), então o cliente
///     só precisa reenviar o documento para <see cref="Target" /> com <see cref="Method" />. Por isso a resposta
///     continua <c>application/hal+json</c>. O corpo é o eco do request do próprio chamador, mais o descritor do
///     <c>rel</c>; nenhum dado do servidor é acrescentado.
/// </summary>
public sealed record HalFormsTemplate
{
    /// <summary>HTTP method name of the <c>QUERY</c> verb (body-carrying query), shared by hypermedia and routing.</summary>
    public const string QueryMethod = "QUERY";

    /// <summary>Método HTTP do envio. Padrão <c>QUERY</c>.</summary>
    [JsonPropertyName("method")]
    public string Method { get; init; } = QueryMethod;

    /// <summary>URI de destino (a mesma <c>href</c> do link correspondente).</summary>
    [JsonPropertyName("target")]
    public required string Target { get; init; }

    /// <summary>Content-Type do corpo. Padrão <c>application/json</c>.</summary>
    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = "application/json";

    /// <summary>Corpo JSON pronto para envio.</summary>
    [JsonPropertyName("body")]
    public required JsonNode Body { get; init; }
}
