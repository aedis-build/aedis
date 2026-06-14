using Aedis.Database.Abstractions;

namespace Aedis.Database.Abstractions;

public interface IWriteRepository<TEntity, TId>
    where TEntity : class
    where TId : notnull
{
    /// <summary>
    ///     Salva a entidade criando sua própria UnitOfWork.
    /// </summary>
    Task<TEntity> SaveAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Salva a entidade usando uma UnitOfWork externa (para transações compartilhadas/Saga).
    /// </summary>
    Task<TEntity> SaveAsync(TEntity entity, IUnitOfWork unitOfWork, CancellationToken ct = default);

    Task DeleteAsync(TId id, CancellationToken ct = default);
    Task DeleteAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default);

    Task BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    Task BulkInsertAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork, CancellationToken ct = default);

    /// <summary>
    ///     Bulk upsert em chunks de tamanho configurado por <c>DatabaseOptions.BulkInsertChunkSize</c>.
    ///     Cria a tabela temporária uma única vez e a reutiliza com TRUNCATE entre chunks,
    ///     reduzindo overhead de catálogo em comparação com múltiplas chamadas a <see cref="BulkInsertAsync"/>.
    /// </summary>
    Task BulkInsertChunkedAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    /// <inheritdoc cref="BulkInsertChunkedAsync(IEnumerable{TEntity},CancellationToken)"/>
    Task BulkInsertChunkedAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork, CancellationToken ct = default);

    Task BulkUpdateAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
    Task BulkUpdateAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork, CancellationToken ct = default);

    Task<int> CommandAsync(ICriteria<TEntity> criteria, CancellationToken ct = default);
    Task<int> CommandAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork, CancellationToken ct = default);
}