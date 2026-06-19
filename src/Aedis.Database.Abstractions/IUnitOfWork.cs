using System.Data.Common;

namespace Aedis.Database.Abstractions;

/// <summary>
///     Sessão transacional sobre uma conexão de banco: expõe consulta e execução parametrizadas e o
///     controle de commit/rollback. Obtenha-a por <see cref="IUnitOfWorkFactory" /> e descarte-a ao fim
///     (o descarte sem commit reverte a transação). As consultas usam parâmetros nomeados (bind parameters),
///     imunes a SQL injection nos valores.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Indica se a sessão é somente leitura — nesse caso <see cref="ExecuteAsync" /> é rejeitado.</summary>
    bool IsReadOnly { get; }

    /// <summary>Executa o SQL e retorna a primeira linha mapeada para <typeparamref name="T" />, ou o default.</summary>
    Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);

    /// <summary>Executa o SQL e retorna todas as linhas mapeadas para <typeparamref name="T" />.</summary>
    Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);

    /// <summary>Executa um comando de escrita e retorna o número de linhas afetadas.</summary>
    Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken ct = default);

    /// <summary>Inicia uma transação na sessão; lança se já houver uma transação em andamento.</summary>
    Task<DbTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Confirma a transação atual; no-op se não houver transação ativa.</summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>Reverte a transação atual; no-op se não houver transação ativa.</summary>
    Task RollbackAsync(CancellationToken ct = default);

    /// <summary>
    ///     Verifica se há uma transação ativa.
    /// </summary>
    bool HasActiveTransaction();
}