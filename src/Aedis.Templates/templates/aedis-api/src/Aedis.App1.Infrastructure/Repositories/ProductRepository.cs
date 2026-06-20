using Aedis.App1.Application.Abstractions;
using Aedis.App1.Domain.Entities;
using Aedis.App1.Infrastructure.Queries;
using Aedis.Database.Abstractions;

namespace Aedis.App1.Infrastructure.Repositories;

/// <summary>
///     Implementação PostgreSQL de <see cref="IProductRepository" />. Compõe o repositório genérico do Aedis
///     (<see cref="IRepository{TEntity,TId}" />, registrado por <c>AddAedisPostgres</c>) para o CRUD base e
///     acrescenta as consultas específicas do agregado via <see cref="ProductSearchCriteria" /> e
///     <see cref="ProductByCodeCriteria" />.
/// </summary>
public sealed class ProductRepository : IProductRepository {
    private readonly IRepository<Product, Guid> _repository;

    /// <summary>
    ///     Cria o repositório compondo o repositório genérico do Aedis.
    /// </summary>
    /// <param name="repository">Repositório genérico do agregado, provido pelo provider PostgreSQL.</param>
    public ProductRepository(IRepository<Product, Guid> repository) {
        _repository = repository;
    }

    /// <inheritdoc />
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) {
        return _repository.GetByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Product?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) {
        var found = await _repository.FindAsync(new ProductByCodeCriteria(code), cancellationToken);
        return found.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Product> Items, int Total)> SearchAsync(string? code, string? name, int page, int pageSize, CancellationToken cancellationToken = default) {
        var items = await _repository.FindAsync(new ProductSearchCriteria(code, name, page, pageSize), cancellationToken);
        var total = await _repository.CountAsync(new ProductSearchCriteria(code, name), cancellationToken);
        return (items.ToList(), total);
    }

    /// <inheritdoc />
    public Task<Product> SaveAsync(Product product, CancellationToken cancellationToken = default) {
        return _repository.SaveAsync(product, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) {
        return _repository.DeleteAsync(id, cancellationToken);
    }
}
