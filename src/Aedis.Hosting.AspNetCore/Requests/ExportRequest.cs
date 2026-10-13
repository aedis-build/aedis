using Aedis.Hosting.AspNetCore.Hypermedia;

namespace Aedis.Hosting.AspNetCore.Requests;

/// <summary>Escopo de uma exportação de coleção.</summary>
public enum CollectionExportMode
{
    /// <summary>Exporta somente a página atual.</summary>
    Page = 0,

    /// <summary>Exporta todos os registros, paginando internamente.</summary>
    All = 1
}

/// <summary>Descritor de exportação carregado no request via <see cref="IExportable" />: formato do arquivo e escopo.</summary>
public sealed record ExportRequest
{
    /// <summary>Formato do arquivo (<see cref="ExportFormat.Xlsx" /> ou <see cref="ExportFormat.Csv" />).</summary>
    public ExportFormat Type { get; init; }

    /// <summary>Escopo: a página atual ou todos os registros.</summary>
    public CollectionExportMode Mode { get; init; }
}

/// <summary>
///     Base opcional para requests de consulta exportáveis: implementa <see cref="IExportable" /> para o DTO
///     só herdar. DTOs que já herdam de outra base implementam a interface diretamente.
/// </summary>
public abstract record ExportableRequest : IExportable
{
    /// <inheritdoc />
    public ExportRequest? Export { get; set; }
}

/// <summary>
///     Base recomendada para consultas de coleção: paginação (<see cref="IPageable" />) e exportação
///     (<see cref="IExportable" />) num único contrato, servindo o mesmo DTO em <c>GET</c> (<c>[FromQuery]</c>)
///     e <c>QUERY</c> (<c>[FromBody]</c>). Com ela a hipermídia monta os <c>_templates</c> de navegação e de
///     export sem convenção de nomes.
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
