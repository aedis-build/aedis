using Aedis.Database.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Aedis.Database.MySql;

/// <summary>
///     Cria sessões de escrita (endpoint primário, com transação) e de leitura (réplicas em round-robin, em
///     autocommit — sem transação) sobre MySQL. Aplica pool e timeouts da <see cref="DatabaseOptions" /> à
///     connection string via <see cref="MySqlConnectionStringBuilder" />.
/// </summary>
public sealed class UnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly ILogger<UnitOfWork> _sessionLogger;
    private readonly DatabaseOptions _options;
    private int _readIndex = -1;

    /// <summary>Cria a fábrica a partir das opções do provider e do logger repassado às sessões geradas.</summary>
    /// <param name="options">Opções do provider MySQL (connection strings, pool, timeouts).</param>
    /// <param name="sessionLogger">Logger injetado em cada <see cref="UnitOfWork" /> criado.</param>
    public UnitOfWorkFactory(IOptions<DatabaseOptions> options, ILogger<UnitOfWork> sessionLogger) {
        _options = options.Value;
        _sessionLogger = sessionLogger;
    }

    /// <inheritdoc />
    public async Task<IUnitOfWork> CreateWriteSessionAsync(CancellationToken ct = default) {
        var raw = _options.WriteConnectionString ?? _options.ConnectionString
            ?? throw new InvalidOperationException("WriteConnectionString ou ConnectionString deve ser configurado.");

        var connection = new MySqlConnection(BuildConnectionString(raw, true));
        await connection.OpenAsync(ct);

        var unitOfWork = new UnitOfWork(connection, false, _sessionLogger);
        await unitOfWork.BeginTransactionAsync(ct);
        return unitOfWork;
    }

    /// <inheritdoc />
    public async Task<IUnitOfWork> CreateReadSessionAsync(CancellationToken ct = default) {
        var raw = NextReadConnectionString() ?? _options.ConnectionString ?? _options.WriteConnectionString
            ?? throw new InvalidOperationException("ConnectionString deve ser configurado.");

        var connection = new MySqlConnection(BuildConnectionString(raw, false));
        await connection.OpenAsync(ct);

        return new UnitOfWork(connection, true, _sessionLogger);
    }

    private string? NextReadConnectionString() {
        var replicas = _options.ReadConnectionStrings;
        if (replicas is null or { Length: 0 })
            return null;

        var index = (int)((uint)Interlocked.Increment(ref _readIndex) % (uint)replicas.Length);
        return replicas[index];
    }

    private string BuildConnectionString(string connectionString, bool isWrite) {
        var builder = new MySqlConnectionStringBuilder(connectionString) {
            ConnectionTimeout = (uint)_options.ConnectionTimeout.TotalSeconds,
            DefaultCommandTimeout = (uint)_options.CommandTimeout.TotalSeconds,
            MaximumPoolSize = (uint)(isWrite ? _options.WritePoolSize : _options.ReadPoolSize)
        };
        return builder.ConnectionString;
    }
}
