using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Hosting.AspNetCore.Hypermedia;

/// <summary>
///     Implementação de <see cref="ILinkMap" /> que resolve URLs por <em>action</em> do controller atual via
///     <c>LinkGenerator</c>. O gerador é resolvido sob demanda (só quando há link por action), de modo que
///     builders que usam apenas <see cref="Raw" /> não dependem dele.
/// </summary>
internal sealed class LinkMap : ILinkMap {
    private readonly HttpContext _httpContext;
    private readonly string? _controller;
    private LinkGenerator? _linkGenerator;

    internal LinkMap(HttpContext httpContext) {
        _httpContext = httpContext;
        _controller = httpContext.GetRouteValue("controller") as string;
    }

    internal List<Link> Entries { get; } = [];

    private LinkGenerator LinkGenerator => _linkGenerator ??= _httpContext.RequestServices.GetRequiredService<LinkGenerator>();

    public ILinkMap Self(string action, object? routeValues = null) => Add("self", "GET", action, routeValues);

    public ILinkMap Collection(string action, object? routeValues = null) => Add("collection", "GET", action, routeValues);

    public ILinkMap Action(string rel, string method, string action, object? routeValues = null) => Add(rel, method, action, routeValues);

    public ILinkMap Get(string rel, string action, object? routeValues = null) => Add(rel, "GET", action, routeValues);

    public ILinkMap Post(string rel, string action, object? routeValues = null) => Add(rel, "POST", action, routeValues);

    public ILinkMap Put(string rel, string action, object? routeValues = null) => Add(rel, "PUT", action, routeValues);

    public ILinkMap Delete(string rel, string action, object? routeValues = null) => Add(rel, "DELETE", action, routeValues);

    public ILinkMap Raw(string rel, string href, string method = "GET", bool templated = false) {
        Entries.Add(new Link(href, rel, method, templated));
        return this;
    }

    private ILinkMap Add(string rel, string method, string action, object? routeValues) {
        var href = LinkGenerator.GetUriByAction(_httpContext, action, _controller, routeValues)
                   ?? throw new InvalidOperationException(
                       $"Não foi possível gerar a URL para a action '{action}' do controller '{_controller}'. " +
                       "Verifique o nome da action e os route values.");
        Entries.Add(new Link(href, rel, method));
        return this;
    }
}
