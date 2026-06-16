using System.Data;
using System.Data.Common;
using Aedis.Database.Abstractions;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Aedis.Database.Postgres;

/// <summary>
///     Sessão transacional sobre uma <see cref="DbConnection" /> (Npgsql), com acesso via Dapper. Expõe a
///     conexão subjacente (<see cref="GetConnection" />) para o caminho de alta performance do COPY. No
///     descarte sem commit/rollback, faz rollback automático.
/// </summary>
public sealed class UnitOfWork(DbConnection connection, bool isReadOnly, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    private readonly DbConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    private bool _disposed;
    private DbTransaction? _transaction;

    public bool IsReadOnly { get; } = isReadOnly;

    public async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null,
        CancellationToken ct = default) {
        EnsureOpen();
        return await _connection.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(sql, parameters, _transaction, cancellationToken: ct));
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null,
        CancellationToken ct = default) {
        EnsureOpen();
        return await _connection.QueryAsync<T>(
            new CommandDefinition(sql, parameters, _transaction, cancellationToken: ct));
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken ct = default) {
        if (IsReadOnly)
            throw new InvalidOperationException("Não é possível executar escrita em uma sessão somente leitura.");

        EnsureOpen();
        return await _connection.ExecuteAsync(
            new CommandDefinition(sql, parameters, _transaction, cancellationToken: ct));
    }

    public async Task<DbTransaction> BeginTransactionAsync(CancellationToken ct = default) {
        if (_transaction != null)
            throw new InvalidOperationException("Transação já iniciada.");

        EnsureOpen();
        _transaction = await _connection.BeginTransactionAsync(ct);
        logger.LogDebug("Transação iniciada (ReadOnly: {IsReadOnly}).", IsReadOnly);
        return _transaction;
    }

    public async Task CommitAsync(CancellationToken ct = default) {
        if (_transaction is null) return;
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
        logger.LogDebug("Transação confirmada.");
    }

    public async Task RollbackAsync(CancellationToken ct = default) {
        if (_transaction is null) return;
        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
        logger.LogDebug("Transação revertida.");
    }

    public bool HasActiveTransaction() => _transaction != null;

    /// <summary>Expõe a conexão subjacente para operações específicas do provider (ex.: PostgreSQL COPY).</summary>
    public DbConnection GetConnection() {
        EnsureOpen();
        return _connection;
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;

        if (_transaction != null) {
            logger.LogWarning("Transação não confirmada nem revertida — revertendo automaticamente.");
            try {
                await _transaction.RollbackAsync();
            }
            catch (Exception ex) {
                logger.LogError(ex, "Erro ao reverter a transação no descarte.");
            }
            finally {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        if (_connection.State != ConnectionState.Closed)
            await _connection.CloseAsync();
        await _connection.DisposeAsync();

        _disposed = true;
    }

    /// <summary>Atribui uma transação já iniciada (usado pela factory nas sessões somente leitura).</summary>
    internal void SetTransaction(DbTransaction transaction) {
        if (_transaction != null)
            throw new InvalidOperationException("Transação já atribuída.");
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    private void EnsureOpen() {
        if (_connection.State != ConnectionState.Open)
            _connection.Open();
    }
}
