using Microsoft.AspNetCore.Http;

namespace Aedis.Hosting.AspNetCore.Hypermedia;

/// <summary>
///     Base para declarar os links de um tipo de resposta. Subclasse e implemente <see cref="Configure" />
///     usando o coletor fluente <see cref="ILinkMap" /> — sem construtor próprio e sem montar URLs. Exemplo:
///     <code>
///     public sealed class ProductLinks : ResourceLinks&lt;ProductResponse&gt; {
///         protected override void Configure(ILinkMap links, ProductResponse p) {
///             links.Self("GetById", new { id = p.Id });
///             links.Action("update", "PUT", "Update", new { id = p.Id });
///             links.Collection("Search");
///         }
///     }
///     </code>
/// </summary>
/// <typeparam name="T">Tipo do modelo de resposta.</typeparam>
public abstract class ResourceLinks<T> : IResourceLinks<T> {
    /// <inheritdoc />
    public Resource<T> Build(T model, HttpContext httpContext) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(httpContext);

        var map = new LinkMap(httpContext);
        Configure(map, model);

        var resource = new Resource<T>(model);
        foreach (var link in map.Entries) {
            resource.AddLink(link.Rel, link.Href, link.Method, link.Templated);
        }

        return resource;
    }

    /// <summary>
    ///     Declara os links do recurso para o <paramref name="model" /> informado, usando o coletor
    ///     <paramref name="links" />.
    /// </summary>
    /// <param name="links">Coletor fluente de links.</param>
    /// <param name="model">Modelo de resposta sendo representado.</param>
    protected abstract void Configure(ILinkMap links, T model);
}
