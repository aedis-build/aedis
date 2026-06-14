using Aedis.Database.Abstractions;

namespace Aedis.Database.Abstractions;

public interface IReadRepository<TEntity, TId>
    where TEntity : class
    where TId : notnull
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default);
    Task<TEntity?> GetByIdAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default);

    Task<IEnumerable<TEntity>> FindAsync(ICriteria<TEntity> criteria, CancellationToken ct = default);

    Task<IEnumerable<TEntity>> FindAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default);

    Task<int> CountAsync(ICriteria<TEntity> criteria, CancellationToken ct = default);
    Task<int> CountAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork, CancellationToken ct = default);

    Task<bool> ExistsAsync(TId id, CancellationToken ct = default);
    Task<bool> ExistsAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default);


    Task<IEnumerable<TResult>> QueryAsync<TResult>(ICriteria<TResult> criteria, CancellationToken ct = default);

    Task<IEnumerable<TResult>> QueryAsync<TResult>(ICriteria<TResult> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default);
}