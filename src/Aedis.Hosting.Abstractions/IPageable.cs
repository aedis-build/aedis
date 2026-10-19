namespace Aedis.Hosting.Abstractions;

/// <summary>
///     Marks a query request as paged. Instead of each DTO declaring <c>Page</c>/<c>PageSize</c> by
///     convention, the contract makes the fields discoverable: hypermedia builds the <c>body</c> of the
///     navigation <c>_templates</c> (<c>self</c>/<c>first</c>/<c>prev</c>/<c>next</c>/<c>last</c>) with the API's
///     real serialization, honoring the naming policy and <c>[JsonPropertyName]</c>. Without the contract the
///     keys <c>page</c>/<c>pageSize</c> are assumed. DTOs with no other base inherit from
///     <see cref="PagedExportableRequest" />.
/// </summary>
public interface IPageable
{
    /// <summary>Requested page (1-based). <c>null</c> = application default.</summary>
    int? Page { get; set; }

    /// <summary>Requested page size. <c>null</c> = application default.</summary>
    int? PageSize { get; set; }
}

/// <summary>
///     Marks a query request as exportable. When <see cref="Export" /> is filled, the call produces a file
///     instead of the paged listing. The descriptor travels in the request itself — body on <c>QUERY</c>,
///     <c>export.type</c>/<c>export.mode</c> on the query string on <c>GET</c> — never personal data in the URL.
/// </summary>
public interface IExportable
{
    /// <summary>Export descriptor. <c>null</c> = regular query; filled = export (format + scope).</summary>
    ExportRequest? Export { get; set; }
}
