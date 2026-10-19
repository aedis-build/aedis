using Aedis.Hosting.Abstractions;

namespace Aedis.Hypermedia.Abstractions;

/// <summary>
///     Quais links de exportação uma coleção anuncia: <c>export-all-{formato}</c> (todos os registros) e
///     <c>export-page-{formato}</c> (a página atual, só quando há paginação). Em <c>GET</c> o link é uma URL
///     autossuficiente (<c>export.type</c>/<c>export.mode</c>); em <c>QUERY</c> a URL fica limpa e o
///     <c>_templates[rel].body</c> traz o request ecoado mais o descritor.
/// </summary>
public sealed record ExportLinkOptions
{
    /// <summary>Formatos anunciados. Padrão: só XLSX.</summary>
    public IReadOnlyCollection<ExportFormat> Formats { get; init; } = [ExportFormat.Xlsx];

    /// <summary>Anuncia <c>export-all-{formato}</c>. Padrão: sim.</summary>
    public bool IncludeAllExport { get; init; } = true;

    /// <summary>Anuncia <c>export-page-{formato}</c> quando <c>page</c>/<c>pageSize</c> estão presentes. Padrão: sim.</summary>
    public bool IncludePageExport { get; init; } = true;

    /// <summary>Só XLSX, nos dois escopos.</summary>
    public static ExportLinkOptions XlsxOnly { get; } = new();

    /// <summary>CSV e XLSX, nos dois escopos.</summary>
    public static ExportLinkOptions CsvAndXlsx { get; } = new() { Formats = [ExportFormat.Csv, ExportFormat.Xlsx] };
}
