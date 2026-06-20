using Microsoft.AspNetCore.Http;

namespace Aedis.Hosting.AspNetCore.Hateoas;

/// <summary>
///     Implementação genérica de <see cref="ICollectionLinkBuilder{T}" /> que gera os links de navegação de uma
///     coleção paginada (<c>self</c>, <c>first</c>, <c>prev</c>, <c>next</c>, <c>last</c>) a partir do caminho
///     base da coleção e dos parâmetros de paginação. Quando o total é conhecido, calcula a última página com
///     precisão; quando não é, infere a existência de próxima página pelo tamanho da página atual.
/// </summary>
/// <typeparam name="T">Tipo de cada item da coleção.</typeparam>
public class DefaultCollectionLinkBuilder<T> : ICollectionLinkBuilder<T> {
    private readonly string _basePath;

    /// <summary>
    ///     Cria o builder de coleção para um recurso cujo caminho base é <paramref name="basePath" />
    ///     (por exemplo, <c>/v1/products</c>).
    /// </summary>
    /// <param name="basePath">Caminho base da coleção, usado quando a requisição não traz um caminho próprio.</param>
    /// <exception cref="ArgumentException">Quando <paramref name="basePath" /> é vazio.</exception>
    public DefaultCollectionLinkBuilder(string basePath) {
        if (string.IsNullOrWhiteSpace(basePath)) {
            throw new ArgumentException("O caminho base da coleção não pode ser vazio.", nameof(basePath));
        }

        _basePath = basePath;
    }

    /// <inheritdoc />
    public CollectionResource<T> Build(IEnumerable<T> items, HttpContext httpContext, int? page = null, int? pageSize = null, int? totalCount = null) {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(httpContext);

        var list = items.ToList();
        var resource = new CollectionResource<T>(list, totalCount, page, pageSize);

        var baseUrl = httpContext.GetBaseUrl();
        var path = ResolvePath(httpContext);
        var effectivePageSize = pageSize is > 0 ? pageSize : null;

        resource.AddLink("self", BuildUrl(baseUrl, path, page, pageSize));

        if (page is not > 0 || effectivePageSize is null) {
            return resource;
        }

        resource.AddLink("first", BuildUrl(baseUrl, path, 1, pageSize));

        if (page > 1) {
            resource.AddLink("prev", BuildUrl(baseUrl, path, page - 1, pageSize));
        }

        if (totalCount is not null) {
            var lastPage = (int)Math.Ceiling((double)totalCount.Value / effectivePageSize.Value);
            if (page < lastPage) {
                resource.AddLink("next", BuildUrl(baseUrl, path, page + 1, pageSize));
            }

            if (lastPage > 1) {
                resource.AddLink("last", BuildUrl(baseUrl, path, lastPage, pageSize));
            }
        }
        else if (list.Count == effectivePageSize.Value) {
            resource.AddLink("next", BuildUrl(baseUrl, path, page + 1, pageSize));
        }

        return resource;
    }

    private string ResolvePath(HttpContext httpContext) {
        var path = httpContext.Request.Path.Value;
        return string.IsNullOrEmpty(path) ? _basePath : path;
    }

    private static string BuildUrl(string baseUrl, string path, int? page, int? pageSize) {
        var url = $"{baseUrl}{path}";
        if (page is null && pageSize is null) {
            return url;
        }

        var query = new List<string>();
        if (page is not null) {
            query.Add($"page={page}");
        }

        if (pageSize is not null) {
            query.Add($"pageSize={pageSize}");
        }

        return $"{url}?{string.Join('&', query)}";
    }
}
