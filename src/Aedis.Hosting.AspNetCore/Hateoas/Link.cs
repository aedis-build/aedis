using System.Text.Json.Serialization;

namespace Aedis.Hosting.AspNetCore.Hateoas;

/// <summary>
///     Representa um vínculo de hipermídia (HATEOAS) anexado a um recurso. Cada link informa ao cliente para
///     onde ir (<see cref="Href" />), qual a relação semântica (<see cref="Rel" />) e qual verbo HTTP usar
///     (<see cref="Method" />), permitindo que o cliente descubra as ações disponíveis sem conhecer as URLs de
///     antemão. Segue um envelope no estilo HAL, com os links agrupados sob <c>_links</c>.
/// </summary>
public sealed class Link {
    /// <summary>
    ///     Cria um link de hipermídia.
    /// </summary>
    /// <param name="href">URL absoluta ou modelo de URL para onde o link aponta.</param>
    /// <param name="rel">Relação semântica do link (por exemplo, <c>self</c>, <c>next</c>, <c>delete</c>).</param>
    /// <param name="method">Verbo HTTP a usar ao seguir o link. Padrão <c>GET</c>.</param>
    /// <param name="templated">Indica se <paramref name="href" /> é um modelo RFC 6570 (com placeholders).</param>
    public Link(string href, string rel, string method = "GET", bool templated = false) {
        Href = href;
        Rel = rel;
        Method = method;
        Templated = templated;
    }

    /// <summary>
    ///     URL (ou modelo de URL) para onde o link aponta.
    /// </summary>
    [JsonPropertyName("href")]
    public string Href { get; }

    /// <summary>
    ///     Relação semântica entre o recurso atual e o destino do link.
    /// </summary>
    [JsonPropertyName("rel")]
    public string Rel { get; }

    /// <summary>
    ///     Verbo HTTP que o cliente deve usar ao seguir este link. Torna o link acionável, não apenas navegável.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; }

    /// <summary>
    ///     Indica se <see cref="Href" /> contém placeholders a serem preenchidos pelo cliente.
    /// </summary>
    [JsonPropertyName("templated")]
    public bool Templated { get; }
}
