using System.Data.Common;

namespace Aedis.Database.Abstractions;

public interface IUnitOfWork : IAsyncDisposable
{
    bool IsReadOnly { get; }
    Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);

    Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);

    Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken ct = default);

    Task<DbTransaction> BeginTransactionAsync(CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);

    Task RollbackAsync(CancellationToken ct = default);

    /// <summary>
    ///     Verifica se há uma transação ativa.
    /// </summary>
    bool HasActiveTransaction();
}