namespace Aedis.Hypermedia.Abstractions;

/// <summary>
///     Declares the hypermedia links of a response type. Implement <see cref="Configure" /> with the fluent
///     <see cref="ILinkMap" /> collector and register the type with
///     <c>AddAedisHypermedia().Resource&lt;T, TLinks&gt;()</c>. The same implementation serves the single
///     resource and each item of a collection. Example:
///     <code>
///     public sealed class ProductLinks : IResourceLinks&lt;ProductResponse&gt; {
///         public void Configure(ILinkMap links, ProductResponse p) {
///             links.Self("GetById", new { id = p.Id });
///             links.Action("update", "PUT", "Update", new { id = p.Id });
///             links.Collection("Search");
///         }
///     }
///     </code>
/// </summary>
/// <typeparam name="T">Response model type.</typeparam>
public interface IResourceLinks<in T>
{
    /// <summary>
    ///     Declares the links of the resource that represents <paramref name="model" />, using the
    ///     <paramref name="links" /> collector.
    /// </summary>
    /// <param name="links">Fluent link collector.</param>
    /// <param name="model">Response model being represented.</param>
    void Configure(ILinkMap links, T model);
}
