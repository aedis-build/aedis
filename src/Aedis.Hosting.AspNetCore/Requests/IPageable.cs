namespace Aedis.Hosting.AspNetCore.Requests;

/// <summary>
///     Marca um request de consulta como paginado. Em vez de cada DTO declarar <c>Page</c>/<c>PageSize</c> por
///     convenção, o contrato torna os campos descobríveis: a hipermídia monta o <c>body</c> dos <c>_templates</c>
///     de navegação (<c>self</c>/<c>first</c>/<c>prev</c>/<c>next</c>/<c>last</c>) na serialização real da API,
///     respeitando política de nomes e <c>[JsonPropertyName]</c>. Sem o contrato, assume as chaves
///     <c>page</c>/<c>pageSize</c>. DTOs sem outra base herdam de <see cref="PagedExportableRequest" />.
/// </summary>
public interface IPageable
{
    /// <summary>Página solicitada (1-based). <c>null</c> = padrão da aplicação.</summary>
    int? Page { get; set; }

    /// <summary>Tamanho da página solicitado. <c>null</c> = padrão da aplicação.</summary>
    int? PageSize { get; set; }
}

/// <summary>
///     Marca um request de consulta como exportável. Quando <see cref="Export" /> vem preenchido, a chamada
///     produz um arquivo em vez da listagem paginada. O descritor viaja no próprio request — corpo no
///     <c>QUERY</c>, <c>export.type</c>/<c>export.mode</c> na query string no <c>GET</c> — nunca dado pessoal na URL.
/// </summary>
public interface IExportable
{
    /// <summary>Descritor de exportação. <c>null</c> = consulta normal; preenchido = exportação (formato + escopo).</summary>
    ExportRequest? Export { get; set; }
}
