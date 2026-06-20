using Aedis.Database.Abstractions;

namespace Aedis.Database.Abstractions;

/// <summary>
///     Contrato de escrita de uma entidade: salvar (upsert), excluir (físico ou soft-delete por convenção)
///     e operações em massa (bulk insert/upsert/update). Cada operação tem uma sobrecarga que cria a própria
///     sessão transacional e outra que recebe uma <see cref="IUnitOfWork" /> externa, para compor com uma
///     transação compartilhada (ex.: Saga).
/// </summary>
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

    /// <summary>
    ///     Exclui a entidade pela chave. Quando a entidade tem soft-delete por convenção (propriedade
    ///     <c>IsDeleted</c>), marca como deletada (e carimba quem/quando, se houver contexto de auditoria);
    ///     caso contrário, faz <c>DELETE</c> físico.
    /// </summary>
    Task DeleteAsync(TId id, CancellationToken ct = default);

    /// <inheritdoc cref="DeleteAsync(TId,CancellationToken)" />
    Task DeleteAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default);

    /// <summary>Insere (ou faz upsert) o conjunto de entidades em uma única passagem de alta performance.</summary>
    Task BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    /// <inheritdoc cref="BulkInsertAsync(IEnumerable{TEntity},CancellationToken)" />
    Task BulkInsertAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork, CancellationToken ct = default);

    /// <summary>
    ///     Bulk upsert em chunks de tamanho configurado por <c>DatabaseOptions.BulkInsertChunkSize</c>.
    ///     Cria a tabela temporária uma única vez e a reutiliza com TRUNCATE entre chunks,
    ///     reduzindo overhead de catálogo em comparação com múltiplas chamadas a <c>BulkInsertAsync</c>.
    /// </summary>
    Task BulkInsertChunkedAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    /// <inheritdoc cref="BulkInsertChunkedAsync(IEnumerable{TEntity},CancellationToken)"/>
    Task BulkInsertChunkedAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork, CancellationToken ct = default);

    /// <summary>Atualiza em massa as entidades informadas, casando pela chave.</summary>
    Task BulkUpdateAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    /// <inheritdoc cref="BulkUpdateAsync(IEnumerable{TEntity},CancellationToken)" />
    Task BulkUpdateAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork, CancellationToken ct = default);

    /// <summary>
    ///     Executa um comando de escrita (UPDATE/DELETE arbitrário) montado por um <see cref="ICriteria{TEntity}" />
    ///     e retorna o número de linhas afetadas.
    /// </summary>
    Task<int> CommandAsync(ICriteria<TEntity> criteria, CancellationToken ct = default);

    /// <inheritdoc cref="CommandAsync(ICriteria{TEntity},CancellationToken)" />
    Task<int> CommandAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork, CancellationToken ct = default);
}