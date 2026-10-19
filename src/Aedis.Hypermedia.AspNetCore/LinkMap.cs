using Aedis.Hypermedia.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Hypermedia.AspNetCore;

/// <summary>
///     <see cref="ILinkMap" /> implementation that resolves URLs per <em>action</em> of the current controller
///     through <c>LinkGenerator</c> and attaches each link straight to the target resource. The generator is
///     resolved on demand (only when an action link is declared), so providers that use only
///     <see cref="Raw" /> do not depend on it.
/// </summary>
internal sealed class LinkMap : ILinkMap
{
    private readonly HttpContext _httpContext;
    private readonly Resource _target;
    private readonly string? _controller;
    private LinkGenerator? _linkGenerator;

    internal LinkMap(HttpContext httpContext, Resource target) {
        _httpContext = httpContext;
        _target = target;
        _controller = httpContext.GetRouteValue("controller") as string;
    }

    private LinkGenerator LinkGenerator => _linkGenerator ??= _httpContext.RequestServices.GetRequiredService<LinkGenerator>();

    public ILinkMap Self(string action, object? routeValues = null) => Add("self", "GET", action, routeValues);

    public ILinkMap Collection(string action, object? routeValues = null) => Add("collection", "GET", action, routeValues);

    public ILinkMap Action(string rel, string method, string action, object? routeValues = null) => Add(rel, method, action, routeValues);

    public ILinkMap Get(string rel, string action, object? routeValues = null) => Add(rel, "GET", action, routeValues);

    public ILinkMap Post(string rel, string action, object? routeValues = null) => Add(rel, "POST", action, routeValues);

    public ILinkMap Put(string rel, string action, object? routeValues = null) => Add(rel, "PUT", action, routeValues);

    public ILinkMap Delete(string rel, string action, object? routeValues = null) => Add(rel, "DELETE", action, routeValues);

    public ILinkMap Raw(string rel, string href, string method = "GET", bool templated = false) {
        _target.AddLink(rel, href, method, templated);
        return this;
    }

    private ILinkMap Add(string rel, string method, string action, object? routeValues) {
        var href = LinkGenerator.GetUriByAction(_httpContext, action, _controller, routeValues)
                   ?? throw new InvalidOperationException(
                       $"Could not generate the URL for action '{action}' of controller '{_controller}'. " +
                       "Check the action name and the route values.");
        _target.AddLink(rel, href, method);
        return this;
    }
}
