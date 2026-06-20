using Aedis.App1.Domain.Entities;
using Aedis.Database.Postgres.Queries;

namespace Aedis.App1.Infrastructure.Queries;

/// <summary>
///     Critério de busca de produtos ativos, com filtros opcionais por código e nome. A paginação é aplicada
///     apenas quando <paramref name="page" /> e <paramref name="pageSize" /> são informados — sem eles, o mesmo
///     critério serve à contagem total (a contagem envolve o SQL em <c>SELECT count(*)</c>, então não deve
///     carregar <c>ORDER BY</c>/<c>LIMIT</c>).
/// </summary>
public sealed class ProductSearchCriteria : PostgresCriteria<Product> {
    /// <summary>
    ///     Monta o critério de busca.
    /// </summary>
    /// <param name="code">Filtro exato por código, opcional.</param>
    /// <param name="name">Filtro parcial por nome, opcional.</param>
    /// <param name="page">Página solicitada (1-based); quando nulo, não pagina (uso em contagem).</param>
    /// <param name="pageSize">Tamanho da página; quando nulo, não pagina (uso em contagem).</param>
    public ProductSearchCriteria(string? code, string? name, int? page = null, int? pageSize = null) : base("product") {
        WhereEquals("is_deleted", false);

        if (!string.IsNullOrWhiteSpace(code)) {
            And().WhereEquals("code", code);
        }

        if (!string.IsNullOrWhiteSpace(name)) {
            And().WhereLike("name", $"%{name}%");
        }

        if (page is not null && pageSize is not null) {
            OrderByDescending("created_at").Page(page.Value, pageSize.Value);
        }
    }
}
