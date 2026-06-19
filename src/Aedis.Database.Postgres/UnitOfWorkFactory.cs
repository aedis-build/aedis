using System.Data;
using System.Data.Common;
using Aedis.Database.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aedis.Database.Postgres;

/// <summary>
///     Cria sessões de escrita (endpoint primário, com transação) e de leitura (réplicas em round-robin,
///     transação READ ONLY / READ COMMITTED) sobre PostgreSQL. Aplica pool e timeouts da
///     <see cref="DatabaseOptions" /> à connection string.
/// </summary>
public sealed class UnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly ILogger<UnitOfWork> _sessionLogger;
    private readonly DatabaseOptions _options;
    private int _readIndex = -1;

    /// <summary>Cria a fábrica a partir das opções do provider e do logger repassado às sessões geradas.</summary>
    /// <param name="options">Opções do provider PostgreSQL (connection strings, pool, timeouts).</param>
    /// <param name="sessionLogger">Logger injetado em cada <see cref="UnitOfWork" /> criado.</param>
    public UnitOfWorkFactory(IOptions<DatabaseOptions> options, ILogger<UnitOfWork> sessionLogger) {
        _options = options.Value;
        _sessionLogger = sessionLogger;
    }

    /// <inheritdoc />
    public async Task<IUnitOfWork> CreateWriteSessionAsync(CancellationToken ct = default) {
        var raw = _options.WriteConnectionString ?? _options.ConnectionString
            ?? throw new InvalidOperationException("WriteConnectionString ou ConnectionString deve ser configurado.");

        var connection = new NpgsqlConnection(BuildConnectionString(raw, true));
        await connection.OpenAsync(ct);

        var unitOfWork = new UnitOfWork(connection, false, _sessionLogger);
        await unitOfWork.BeginTransactionAsync(ct);
        return unitOfWork;
    }

    /// <inheritdoc />
    public async Task<IUnitOfWork> CreateReadSessionAsync(CancellationToken ct = default) {
        var raw = NextReadConnectionString() ?? _options.ConnectionString ?? _options.WriteConnectionString
            ?? throw new InvalidOperationException("ConnectionString deve ser configurado.");

        var connection = new NpgsqlConnection(BuildConnectionString(raw, false));
        await connection.OpenAsync(ct);

        var unitOfWork = new UnitOfWork(connection, true, _sessionLogger);
        DbTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        unitOfWork.SetTransaction(transaction);
        return unitOfWork;
    }

    private string? NextReadConnectionString() {
        var replicas = _options.ReadConnectionStrings;
        if (replicas is null or { Length: 0 })
            return null;

        var index = (int)((uint)Interlocked.Increment(ref _readIndex) % (uint)replicas.Length);
        return replicas[index];
    }

    private string BuildConnectionString(string connectionString, bool isWrite) {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) {
            Timeout = (int)_options.ConnectionTimeout.TotalSeconds,
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
            MaxPoolSize = isWrite ? _options.WritePoolSize : _options.ReadPoolSize
        };
        return builder.ConnectionString;
    }
}
