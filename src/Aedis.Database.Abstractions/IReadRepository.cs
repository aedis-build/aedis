using Aedis.Database.Abstractions;

namespace Aedis.Database.Abstractions;

/// <summary>
///     Contrato de leitura de uma entidade: busca por id, consulta por <see cref="ICriteria{TEntity}" />,
///     contagem e existência. Cada operação tem uma sobrecarga que cria a própria sessão de leitura e outra
///     que recebe uma <see cref="IUnitOfWork" /> externa, para compor com uma transação já aberta.
/// </summary>
public interface IReadRepository<TEntity, TId>
    where TEntity : class
    where TId : notnull
{
    /// <summary>Busca a entidade pela chave; retorna <c>null</c> se não existir (ou estiver soft-deletada).</summary>
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default);

    /// <inheritdoc cref="GetByIdAsync(TId,CancellationToken)" />
    Task<TEntity?> GetByIdAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default);

    /// <summary>Retorna todas as entidades que satisfazem o critério.</summary>
    Task<IEnumerable<TEntity>> FindAsync(ICriteria<TEntity> criteria, CancellationToken ct = default);

    /// <inheritdoc cref="FindAsync(ICriteria{TEntity},CancellationToken)" />
    Task<IEnumerable<TEntity>> FindAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default);

    /// <summary>Conta as entidades que satisfazem o critério (envolve a contagem em subquery quando há DISTINCT).</summary>
    Task<int> CountAsync(ICriteria<TEntity> criteria, CancellationToken ct = default);

    /// <inheritdoc cref="CountAsync(ICriteria{TEntity},CancellationToken)" />
    Task<int> CountAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork, CancellationToken ct = default);

    /// <summary>Indica se existe alguma entidade com a chave informada.</summary>
    Task<bool> ExistsAsync(TId id, CancellationToken ct = default);

    /// <inheritdoc cref="ExistsAsync(TId,CancellationToken)" />
    Task<bool> ExistsAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default);

    /// <summary>
    ///     Executa um critério projetando para um tipo de resultado arbitrário (<typeparamref name="TResult" />),
    ///     útil para DTOs e projeções que não correspondem à entidade.
    /// </summary>
    Task<IEnumerable<TResult>> QueryAsync<TResult>(ICriteria<TResult> criteria, CancellationToken ct = default);

    /// <inheritdoc cref="QueryAsync{TResult}(ICriteria{TResult},CancellationToken)" />
    Task<IEnumerable<TResult>> QueryAsync<TResult>(ICriteria<TResult> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default);
}