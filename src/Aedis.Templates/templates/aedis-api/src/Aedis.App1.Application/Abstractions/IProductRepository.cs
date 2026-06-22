using Aedis.App1.Domain.Entities;
using Aedis.Core;

namespace Aedis.App1.Application.Abstractions;

/// <summary>
///     Porta de saída do agregado <see cref="Product" />. A camada de aplicação depende apenas deste contrato;
///     a implementação concreta (PostgreSQL) vive na Infrastructure, mantendo o domínio independente de
///     detalhes de persistência.
/// </summary>
public interface IProductRepository {
    /// <summary>
    ///     Recupera um produto pela identidade, ou <c>null</c> quando não existe (ou está soft-deleted).
    /// </summary>
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Recupera um produto pela chave natural <see cref="Product.Code" />, ou <c>null</c> quando não existe.
    /// </summary>
    Task<Product?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Busca paginada com filtros opcionais por código e nome, retornando a página (itens + total geral).
    /// </summary>
    Task<PagedResult<Product>> SearchAsync(string? code, string? name, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Persiste o produto (upsert) e devolve a entidade com as colunas de auditoria carimbadas.
    /// </summary>
    Task<Product> SaveAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Remove o produto pela identidade (soft-delete quando a entidade tem coluna <c>IsDeleted</c>).
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
