using System.Data;
using System.Data.Common;
using Aedis.Database.Abstractions;
using Aedis.Database.Postgres.Tenancy;
using Aedis.Tenancy.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aedis.Database.Postgres;

/// <summary>
///     Cria sessões de escrita (endpoint primário, com transação) e de leitura (réplicas em round-robin,
///     transação READ ONLY / READ COMMITTED) sobre PostgreSQL. Aplica pool e timeouts da
///     <see cref="DatabaseOptions" /> à connection string. Com um <see cref="ICurrentTenant" /> registrado, cada
///     sessão aberta dentro de um escopo de tenant já nasce com <see cref="PostgresTenancy.SettingName" />
///     definido na transação (base para Row-Level Security).
/// </summary>
public sealed class UnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly ILogger<UnitOfWork> _sessionLogger;
    private readonly DatabaseOptions _options;
    private readonly ICurrentTenant? _currentTenant;
    private int _readIndex = -1;

    /// <summary>Cria a fábrica a partir das opções do provider e do logger repassado às sessões geradas.</summary>
    /// <param name="options">Opções do provider PostgreSQL (connection strings, pool, timeouts).</param>
    /// <param name="sessionLogger">Logger injetado em cada <see cref="UnitOfWork" /> criado.</param>
    /// <param name="currentTenant">Tenant atual opcional, propagado para a variável de sessão de cada transação.</param>
    public UnitOfWorkFactory(IOptions<DatabaseOptions> options, ILogger<UnitOfWork> sessionLogger,
        ICurrentTenant? currentTenant = null) {
        _options = options.Value;
        _sessionLogger = sessionLogger;
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    public async Task<IUnitOfWork> CreateWriteSessionAsync(CancellationToken ct = default) {
        var raw = _options.WriteConnectionString ?? _options.ConnectionString
            ?? throw new InvalidOperationException("WriteConnectionString ou ConnectionString deve ser configurado.");

        var connection = new NpgsqlConnection(BuildConnectionString(raw, true));
        await connection.OpenAsync(ct);

        var unitOfWork = new UnitOfWork(connection, false, _sessionLogger);
        await unitOfWork.BeginTransactionAsync(ct);
        await ApplyTenantAsync(unitOfWork, ct);
        return unitOfWork;
    }

    /// <summary>
    ///     Abre a sessão de leitura em autocommit, sem transação: em <c>ReadCommitted</c> ela não dava consistência
    ///     entre statements e só custava <c>BEGIN</c>/<c>COMMIT</c> e o rollback no descarte. <see cref="IUnitOfWork.HasActiveTransaction" />
    ///     responde <c>false</c>; commit e rollback são no-op.
    /// </summary>
    public async Task<IUnitOfWork> CreateReadSessionAsync(CancellationToken ct = default) {
        var raw = NextReadConnectionString() ?? _options.ConnectionString ?? _options.WriteConnectionString
            ?? throw new InvalidOperationException("ConnectionString deve ser configurado.");

        var connection = new NpgsqlConnection(BuildConnectionString(raw, false));
        await connection.OpenAsync(ct);

        var unitOfWork = new UnitOfWork(connection, true, _sessionLogger);
        // set_config(..., true) só vive dentro de uma transação: com tenant a leitura ganha uma; sem tenant fica em autocommit.
        if (_currentTenant?.Id is not null)
            unitOfWork.SetTransaction(await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct));
        await ApplyTenantAsync(unitOfWork, ct);
        return unitOfWork;
    }

    private async Task ApplyTenantAsync(UnitOfWork unitOfWork, CancellationToken ct) {
        if (_currentTenant?.Id is not { } tenantId)
            return;

        try {
            await unitOfWork.QuerySingleOrDefaultAsync<string>("SELECT set_config(@name, @tenantId, true)",
                new { name = PostgresTenancy.SettingName, tenantId }, ct);
        }
        catch {
            await unitOfWork.DisposeAsync();
            throw;
        }
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
