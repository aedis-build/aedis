using System.Reflection;
using Aedis.Database.Abstractions;
using Aedis.Database.SqlServer.Internal;
using Aedis.Database.SqlServer.Naming;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Aedis.Database.SqlServer;

/// <summary>
///     Motor de bulk insert/upsert de alta performance para SQL Server, em paridade com o COPY do PostgreSQL.
///     Usa <see cref="SqlBulkCopy" /> alimentado por um <see cref="ObjectDataReader{T}" /> em streaming (sem
///     materializar a carga em memória), e faz upsert via tabela de staging temporária + <c>MERGE</c>. No
///     modo chunked com upsert, a staging é criada uma única vez e reaproveitada com <c>TRUNCATE</c> entre os
///     lotes — amortizando o custo de catálogo em cargas de dezenas de milhões.
/// </summary>
public sealed class SqlServerBulkInserter(ILogger<SqlServerBulkInserter> logger) {
    private const int DefaultBatchSize = 5_000;

    /// <summary>
    ///     Insere (ou faz upsert, se <paramref name="upsert" /> for informado) em uma passagem.
    /// </summary>
    public async Task BulkInsertAsync<TEntity>(IUnitOfWork unitOfWork, string tableName, PropertyInfo[] properties,
        IEnumerable<TEntity> entities, NamingStrategyResolver namingResolver, DatabaseOptions options,
        UpsertSpec? upsert = null, CancellationToken ct = default) where TEntity : class {
        var columns = BuildColumns(properties, namingResolver, options);
        var connection = ResolveConnection(unitOfWork);
        var transaction = ResolveTransaction(unitOfWork);

        if (upsert is null) {
            await BulkCopyAsync(connection, transaction, tableName, columns, properties, entities, options.BulkInsertChunkSize, ct);
            return;
        }

        var staging = StagingTableName(tableName);
        await unitOfWork.ExecuteAsync($"SELECT TOP 0 * INTO {staging} FROM {tableName}", null, ct);
        await BulkCopyAsync(connection, transaction, staging, columns, properties, entities, options.BulkInsertChunkSize, ct);
        await unitOfWork.ExecuteAsync(BuildMerge(tableName, staging, columns, upsert, namingResolver, options), null, ct);
        await unitOfWork.ExecuteAsync($"DROP TABLE {staging}", null, ct);
    }

    /// <summary>
    ///     Insere/upserta em lotes. Sem upsert, uma única chamada de <see cref="SqlBulkCopy" /> em streaming
    ///     cobre toda a sequência (batching interno). Com upsert, reaproveita a staging entre os lotes.
    /// </summary>
    public async Task BulkInsertChunkedAsync<TEntity>(IUnitOfWork unitOfWork, string tableName, PropertyInfo[] properties,
        IEnumerable<TEntity> entities, NamingStrategyResolver namingResolver, DatabaseOptions options, int chunkSize,
        UpsertSpec? upsert = null, CancellationToken ct = default) where TEntity : class {
        var columns = BuildColumns(properties, namingResolver, options);
        var connection = ResolveConnection(unitOfWork);
        var transaction = ResolveTransaction(unitOfWork);

        if (upsert is null) {
            await BulkCopyAsync(connection, transaction, tableName, columns, properties, entities, chunkSize, ct);
            return;
        }

        var staging = StagingTableName(tableName);
        await unitOfWork.ExecuteAsync($"SELECT TOP 0 * INTO {staging} FROM {tableName}", null, ct);
        var merge = BuildMerge(tableName, staging, columns, upsert, namingResolver, options);

        var chunkCount = 0;
        foreach (var chunk in Chunk(entities, chunkSize)) {
            await BulkCopyAsync(connection, transaction, staging, columns, properties, chunk, chunkSize, ct);
            await unitOfWork.ExecuteAsync(merge, null, ct);
            await unitOfWork.ExecuteAsync($"TRUNCATE TABLE {staging}", null, ct);
            chunkCount++;
        }

        await unitOfWork.ExecuteAsync($"DROP TABLE {staging}", null, ct);
        logger.LogDebug("BulkInsertChunked (upsert) concluído em {Chunks} chunks.", chunkCount);
    }

    private static async Task BulkCopyAsync<TEntity>(SqlConnection connection, SqlTransaction? transaction,
        string destination, IReadOnlyList<BulkColumn> columns, PropertyInfo[] properties, IEnumerable<TEntity> entities,
        int batchSize, CancellationToken ct) where TEntity : class {
        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, transaction) {
            DestinationTableName = destination,
            BatchSize = batchSize > 0 ? batchSize : DefaultBatchSize,
            BulkCopyTimeout = 0,
            EnableStreaming = true
        };

        for (var i = 0; i < columns.Count; i++) {
            bulkCopy.ColumnMappings.Add(i, columns[i].Column);
        }

        using var reader = new ObjectDataReader<TEntity>(properties, entities);
        await bulkCopy.WriteToServerAsync(reader, ct);
    }

    private static string BuildMerge(string tableName, string staging, IReadOnlyList<BulkColumn> columns,
        UpsertSpec upsert, NamingStrategyResolver namingResolver, DatabaseOptions options) {
        string Column(string property) => ResolveColumn(property, namingResolver, options);

        var allColumns = columns.Select(column => column.Column).ToList();
        var keyColumns = upsert.KeyProperties.Select(Column).ToList();
        var on = string.Join(" AND ", keyColumns.Select(key => $"t.[{key}] = s.[{key}]"));
        var tail = SqlServerUpsertCompiler.BuildMergeTail(upsert, allColumns, keyColumns, Column);

        return $"MERGE INTO {tableName} WITH (HOLDLOCK) AS t USING {staging} AS s ON {on} {tail}";
    }

    private static IReadOnlyList<BulkColumn> BuildColumns(PropertyInfo[] properties, NamingStrategyResolver namingResolver, DatabaseOptions options) =>
        properties.Select(property => new BulkColumn(property, ResolveColumn(property.Name, namingResolver, options))).ToList();

    private static string ResolveColumn(string property, NamingStrategyResolver namingResolver, DatabaseOptions options) {
        var context = NamingContext.ForColumn(options.NamingConvention, property);
        return namingResolver.GetStrategy(context).Convert(context);
    }

    private static string StagingTableName(string tableName) {
        var bare = tableName.Replace("[", string.Empty).Replace("]", string.Empty);
        var last = bare.Contains('.') ? bare[(bare.LastIndexOf('.') + 1)..] : bare;
        return $"#aedis_stg_{last}_{Guid.NewGuid():N}";
    }

    private static SqlConnection ResolveConnection(IUnitOfWork unitOfWork) {
        if (unitOfWork is not UnitOfWork uow) {
            throw new InvalidOperationException("A sessão deve ser uma UnitOfWork do provider SQL Server.");
        }

        if (uow.GetConnection() is not SqlConnection connection) {
            throw new InvalidOperationException("A conexão subjacente não é uma SqlConnection.");
        }

        return connection;
    }

    private static SqlTransaction? ResolveTransaction(IUnitOfWork unitOfWork) =>
        unitOfWork is UnitOfWork uow && uow.GetTransaction() is SqlTransaction transaction ? transaction : null;

    private static IEnumerable<IReadOnlyList<TEntity>> Chunk<TEntity>(IEnumerable<TEntity> source, int size) {
        var chunk = new List<TEntity>(size);
        foreach (var item in source) {
            chunk.Add(item);
            if (chunk.Count >= size) {
                yield return chunk;
                chunk = new List<TEntity>(size);
            }
        }

        if (chunk.Count > 0) {
            yield return chunk;
        }
    }

    private readonly record struct BulkColumn(PropertyInfo Property, string Column);
}
