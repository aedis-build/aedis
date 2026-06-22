namespace Aedis.Hosting.AspNetCore.Hypermedia;

/// <summary>
///     Coletor fluente de links de um recurso, usado dentro de <see cref="ResourceLinks{T}.Configure" />. Os
///     links são resolvidos por <em>action</em> do controller atual (via <c>LinkGenerator</c>), então você
///     declara a ação e os route values — sem montar URLs na mão e sem quebrar ao renomear rotas. Use
///     <see cref="Raw" /> apenas para destinos fora do roteamento de actions.
/// </summary>
public interface ILinkMap {
    /// <summary>
    ///     Adiciona o link <c>self</c> (GET) apontando para a <paramref name="action" /> do controller atual.
    /// </summary>
    /// <param name="action">Nome do método de ação (por exemplo, <c>nameof(GetById)</c>).</param>
    /// <param name="routeValues">Valores de rota que identificam o recurso.</param>
    ILinkMap Self(string action, object? routeValues = null);

    /// <summary>
    ///     Adiciona o link <c>collection</c> (GET) apontando para a ação de listagem do controller atual.
    /// </summary>
    /// <param name="action">Nome do método de ação de listagem.</param>
    /// <param name="routeValues">Valores de rota, quando aplicável.</param>
    ILinkMap Collection(string action, object? routeValues = null);

    /// <summary>
    ///     Adiciona um link de ação com relação e verbo explícitos, apontando para a <paramref name="action" />
    ///     do controller atual.
    /// </summary>
    /// <param name="rel">Relação semântica (por exemplo, <c>update</c>, <c>delete</c>).</param>
    /// <param name="method">Verbo HTTP da ação (por exemplo, <c>PUT</c>, <c>DELETE</c>).</param>
    /// <param name="action">Nome do método de ação.</param>
    /// <param name="routeValues">Valores de rota que identificam o recurso.</param>
    ILinkMap Action(string rel, string method, string action, object? routeValues = null);

    /// <summary>
    ///     Adiciona um link <c>GET</c> com a relação informada.
    /// </summary>
    /// <param name="rel">Relação semântica.</param>
    /// <param name="action">Nome do método de ação.</param>
    /// <param name="routeValues">Valores de rota, quando aplicável.</param>
    ILinkMap Get(string rel, string action, object? routeValues = null);

    /// <summary>
    ///     Adiciona um link <c>POST</c> com a relação informada.
    /// </summary>
    /// <param name="rel">Relação semântica.</param>
    /// <param name="action">Nome do método de ação.</param>
    /// <param name="routeValues">Valores de rota, quando aplicável.</param>
    ILinkMap Post(string rel, string action, object? routeValues = null);

    /// <summary>
    ///     Adiciona um link <c>PUT</c> com a relação informada.
    /// </summary>
    /// <param name="rel">Relação semântica.</param>
    /// <param name="action">Nome do método de ação.</param>
    /// <param name="routeValues">Valores de rota, quando aplicável.</param>
    ILinkMap Put(string rel, string action, object? routeValues = null);

    /// <summary>
    ///     Adiciona um link <c>DELETE</c> com a relação informada.
    /// </summary>
    /// <param name="rel">Relação semântica.</param>
    /// <param name="action">Nome do método de ação.</param>
    /// <param name="routeValues">Valores de rota, quando aplicável.</param>
    ILinkMap Delete(string rel, string action, object? routeValues = null);

    /// <summary>
    ///     Adiciona um link com URL literal — escape para destinos fora do roteamento de actions (outro serviço,
    ///     recurso externo, modelo de URL).
    /// </summary>
    /// <param name="rel">Relação semântica.</param>
    /// <param name="href">URL absoluta ou modelo de URL.</param>
    /// <param name="method">Verbo HTTP. Padrão <c>GET</c>.</param>
    /// <param name="templated">Indica se <paramref name="href" /> é um modelo com placeholders.</param>
    ILinkMap Raw(string rel, string href, string method = "GET", bool templated = false);
}
