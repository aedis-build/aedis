using Microsoft.AspNetCore.Http;

namespace Aedis.Hosting.AspNetCore.Hypermedia;

/// <summary>
///     Gera os links de navegação de uma coleção paginada (<c>self</c>/<c>first</c>/<c>prev</c>/<c>next</c>/
///     <c>last</c>) a partir da própria URL da requisição de listagem. Quando o total é conhecido, calcula a
///     última página com precisão; sem total, infere a próxima página pelo tamanho da página atual.
/// </summary>
internal static class PaginationLinks {
    public static void Apply<T>(ResourceCollection<T> collection, HttpContext httpContext, int? page, int? pageSize, int? totalCount) {
        var request = httpContext.Request;
        var url = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}";
        var effectivePageSize = pageSize is > 0 ? pageSize : null;

        collection.AddLink("self", BuildUrl(url, page, pageSize));

        if (page is not > 0 || effectivePageSize is null) {
            return;
        }

        collection.AddLink("first", BuildUrl(url, 1, pageSize));

        if (page > 1) {
            collection.AddLink("prev", BuildUrl(url, page - 1, pageSize));
        }

        if (totalCount is not null) {
            var lastPage = (int)Math.Ceiling((double)totalCount.Value / effectivePageSize.Value);
            if (page < lastPage) {
                collection.AddLink("next", BuildUrl(url, page + 1, pageSize));
            }

            if (lastPage > 1) {
                collection.AddLink("last", BuildUrl(url, lastPage, pageSize));
            }
        }
        else if (collection.Items.Count == effectivePageSize.Value) {
            collection.AddLink("next", BuildUrl(url, page + 1, pageSize));
        }
    }

    private static string BuildUrl(string url, int? page, int? pageSize) {
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
