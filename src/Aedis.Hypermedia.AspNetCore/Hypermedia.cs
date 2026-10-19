using Aedis.Core;
using Aedis.Hypermedia.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Hypermedia.AspNetCore;

/// <summary>
///     <see cref="IHypermedia" /> over the current <see cref="HttpContext" />: resolves the
///     <see cref="IResourceLinks{T}" /> of each model type from the request services, applies it through a
///     <see cref="LinkMap" /> and, for collections, adds the navigation and export links
///     (<see cref="PaginationLinks" />). Scoped, because links depend on the request (host, path, verb).
/// </summary>
internal sealed class Hypermedia : IHypermedia
{
    private readonly IHttpContextAccessor _accessor;

    public Hypermedia(IHttpContextAccessor accessor) {
        _accessor = accessor;
    }

    private HttpContext Context => _accessor.HttpContext
                                   ?? throw new InvalidOperationException("IHypermedia requires an active HTTP request.");

    public Resource<T>? Resource<T>(T? model) => model is null ? null : Wrap(model, Context);

    public ResourceCollection<T> Collection<T>(PagedResult<T> page, ExportLinkOptions? export = null, object? requestEcho = null) {
        ArgumentNullException.ThrowIfNull(page);

        var context = Context;
        var items = page.Items.Select(item => Wrap(item, context)).ToList();
        var collection = new ResourceCollection<T>(items, page.Total, page.Page, page.PageSize);
        PaginationLinks.Apply(collection, context, page.Page, page.PageSize, page.Total, export, requestEcho);

        return collection;
    }

    private static Resource<T> Wrap<T>(T model, HttpContext context) {
        var resource = new Resource<T>(model);
        context.RequestServices.GetService<IResourceLinks<T>>()?.Configure(new LinkMap(context, resource), model);
        return resource;
    }
}
