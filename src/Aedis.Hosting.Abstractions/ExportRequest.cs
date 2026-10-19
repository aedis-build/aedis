namespace Aedis.Hosting.Abstractions;

/// <summary>Collection export formats announced by hypermedia and accepted by the export.</summary>
public enum ExportFormat
{
    /// <summary>Delimiter-separated text (.csv).</summary>
    Csv,

    /// <summary>Office Open XML spreadsheet (.xlsx).</summary>
    Xlsx
}

/// <summary>Scope of a collection export.</summary>
public enum CollectionExportMode
{
    /// <summary>Exports only the current page.</summary>
    Page = 0,

    /// <summary>Exports every record, paging internally.</summary>
    All = 1
}

/// <summary>Export descriptor carried by the request through <see cref="IExportable" />: file format and scope.</summary>
public sealed record ExportRequest
{
    /// <summary>File format (<see cref="ExportFormat.Xlsx" /> or <see cref="ExportFormat.Csv" />).</summary>
    public ExportFormat Type { get; init; }

    /// <summary>Scope: the current page or every record.</summary>
    public CollectionExportMode Mode { get; init; }
}

/// <summary>
///     Optional base for exportable query requests: implements <see cref="IExportable" /> so the DTO only has to
///     inherit. DTOs that already inherit from another base implement the interface directly.
/// </summary>
public abstract record ExportableRequest : IExportable
{
    /// <inheritdoc />
    public ExportRequest? Export { get; set; }
}

/// <summary>
///     Recommended base for collection queries: paging (<see cref="IPageable" />) and export
///     (<see cref="IExportable" />) in a single contract, serving the same DTO on <c>GET</c> (<c>[FromQuery]</c>)
///     and <c>QUERY</c> (<c>[FromBody]</c>). With it, hypermedia builds the navigation and export
///     <c>_templates</c> without naming conventions.
/// </summary>
/// <example>
///     <code>
///     public sealed record SearchOrdersRequest : PagedExportableRequest {
///         public string? Status { get; init; }
///     }
///     </code>
/// </example>
public abstract record PagedExportableRequest : ExportableRequest, IPageable
{
    /// <inheritdoc />
    public int? Page { get; set; }

    /// <inheritdoc />
    public int? PageSize { get; set; }
}
