using Aedis.Hypermedia.Abstractions;
using Aedis.Hypermedia.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Entry point of the hypermedia plugin. Registers <see cref="IHypermedia" /> (scoped), the
///     <c>IHttpContextAccessor</c> it reads the request from and the <see cref="ActionRequestCaptureFilter" />
///     that echoes the query request in the <c>_templates</c>; then, per response type, the link provider
///     (<see cref="IResourceLinks{T}" />) that serves both the single resource and each collection item.
///     Registration is explicit and deterministic (no reflection scan).
/// </summary>
public static class HypermediaServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the hypermedia services and starts the fluent registration of link providers.
    /// </summary>
    /// <param name="services">Service collection.</param>
    public static IAedisHypermediaBuilder AddAedisHypermedia(this IServiceCollection services) {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.TryAddScoped<IHypermedia, Hypermedia>();
        services.Configure<MvcOptions>(options => options.Filters.Add<ActionRequestCaptureFilter>());

        return new AedisHypermediaBuilder(services);
    }
}

/// <summary>
///     Fluent builder that registers the hypermedia link providers per response type.
/// </summary>
public interface IAedisHypermediaBuilder
{
    /// <summary>
    ///     Underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    ///     Registers the link provider of a response type. Applies to the single resource and to each
    ///     collection item of that type.
    /// </summary>
    /// <typeparam name="TResponse">Response model type.</typeparam>
    /// <typeparam name="TLinks">Link provider implementing <see cref="IResourceLinks{T}" />.</typeparam>
    IAedisHypermediaBuilder Resource<TResponse, TLinks>() where TLinks : class, IResourceLinks<TResponse>;
}

internal sealed class AedisHypermediaBuilder : IAedisHypermediaBuilder
{
    public AedisHypermediaBuilder(IServiceCollection services) {
        Services = services;
    }

    public IServiceCollection Services { get; }

    public IAedisHypermediaBuilder Resource<TResponse, TLinks>() where TLinks : class, IResourceLinks<TResponse> {
        Services.AddScoped<IResourceLinks<TResponse>, TLinks>();
        return this;
    }
}
