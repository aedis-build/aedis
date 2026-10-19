using Aedis.Core;

namespace Aedis.Hypermedia.Abstractions;

/// <summary>
///     Request-scoped service that builds the HAL representation of responses. Controllers (or
///     <c>ApiControllerBase</c> helpers) ask it to wrap a model or a page; the links come from the
///     <see cref="IResourceLinks{T}" /> registered for the model type, so hypermedia is enabled per type
///     without touching the actions. Provided by the <c>Aedis.Hypermedia.AspNetCore</c> plugin.
/// </summary>
public interface IHypermedia
{
    /// <summary>
    ///     Wraps the model in a <see cref="Resource{T}" /> with the <c>_links</c> declared by the registered
    ///     <see cref="IResourceLinks{T}" />. Returns <c>null</c> when <paramref name="model" /> is <c>null</c>;
    ///     when no provider is registered for <typeparamref name="T" />, the resource carries only the data.
    /// </summary>
    /// <typeparam name="T">Response model type.</typeparam>
    /// <param name="model">Model to wrap, or <c>null</c> when the resource does not exist.</param>
    Resource<T>? Resource<T>(T? model);

    /// <summary>
    ///     Builds a <see cref="ResourceCollection{T}" /> from a page: each item is wrapped as a resource and the
    ///     collection receives <c>self</c>/<c>first</c>/<c>prev</c>/<c>next</c>/<c>last</c> (plus the export links
    ///     and <c>_templates</c> when <paramref name="export" /> is given), derived from the current request.
    /// </summary>
    /// <typeparam name="T">Type of each item.</typeparam>
    /// <param name="page">Page of results (items + paging metadata).</param>
    /// <param name="export">Export links to announce; <c>null</c> for none.</param>
    /// <param name="requestEcho">Query request echoed in the <c>_templates</c>; <c>null</c> uses the one captured from the action.</param>
    ResourceCollection<T> Collection<T>(PagedResult<T> page, ExportLinkOptions? export = null, object? requestEcho = null);
}
