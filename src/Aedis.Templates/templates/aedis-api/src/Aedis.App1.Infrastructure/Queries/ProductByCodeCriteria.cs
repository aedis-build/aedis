using Aedis.App1.Domain.Entities;
using Aedis.Database.Postgres.Queries;

namespace Aedis.App1.Infrastructure.Queries;

/// <summary>
///     Critério que localiza um produto ativo pela chave natural <see cref="Product.Code" />. As colunas são
///     referenciadas em <c>snake_case</c>, em paridade com a convenção de nomes do provider PostgreSQL.
/// </summary>
public sealed class ProductByCodeCriteria : PostgresCriteria<Product> {
    /// <summary>
    ///     Cria o critério para um código específico.
    /// </summary>
    /// <param name="code">Código de negócio a localizar.</param>
    public ProductByCodeCriteria(string code) : base("product") {
        WhereEquals("is_deleted", false)
            .And()
            .WhereEquals("code", code)
            .Limit(1);
    }
}
