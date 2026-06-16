using System.Reflection;
using Aedis.Database.Abstractions;
using Aedis.Database.Postgres.Naming;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aedis.Database.Postgres;

/// <summary>
///     Motor de bulk insert/upsert do PostgreSQL via <c>COPY FROM STDIN (FORMAT BINARY)</c> — o caminho
///     de máxima performance do Npgsql (ordens de grandeza acima de INSERTs individuais). Três modos:
///     <list type="bullet">
///         <item>COPY direto (sem conflito);</item>
///         <item>upsert: COPY para tabela temporária + <c>INSERT ... SELECT ... ON CONFLICT</c> atômico;</item>
///         <item>chunked: tabela temporária <em>única</em> reutilizada com <c>TRUNCATE</c> (O(1)) entre
///         chunks — amortiza o overhead de catálogo, essencial para cargas de dezenas de milhões de linhas
///         (ex.: Aurora PostgreSQL com storage distribuído).</item>
///     </list>
///     Enums são gravados como string em maiúsculas; <c>null</c>, <see cref="DateOnly" /> e
///     <see cref="TimeOnly" /> são tratados pelo importador binário.
/// </summary>
public sealed class PostgresBulkInserter(ILogger<PostgresBulkInserter> logger)
{
    public async Task BulkInsertAsync<TEntity>(IUnitOfWork unitOfWork, string tableName, PropertyInfo[] properties,
        IEnumerable<TEntity> entities, NamingStrategyResolver namingResolver, DatabaseOptions options,
        string? onConflictClause = null, CancellationToken ct = default) where TEntity : class {
        var list = entities as IReadOnlyList<TEntity> ?? entities.ToList();
        if (list.Count == 0) return;

        var connection = ResolveConnection(unitOfWork);
        var columns = BuildColumns(properties, namingResolver, options);

        if (onConflictClause != null)
            await UpsertViaTempTableAsync(connection, unitOfWork, tableName, columns, properties, list,
                onConflictClause, ct);
        else
            await CopyAsync(connection, tableName, columns, properties, list, ct);
    }

    public async Task BulkInsertChunkedAsync<TEntity>(IUnitOfWork unitOfWork, string tableName,
        PropertyInfo[] properties, IEnumerable<TEntity> entities, NamingStrategyResolver namingResolver,
        DatabaseOptions options, int chunkSize, string? onConflictClause = null, CancellationToken ct = default)
        where TEntity : class {
        var list = entities as IReadOnlyList<TEntity> ?? entities.ToList();
        if (list.Count == 0) return;

        var connection = ResolveConnection(unitOfWork);
        var columns = BuildColumns(properties, namingResolver, options);

        if (onConflictClause is null) {
            foreach (var chunk in Chunk(list, chunkSize))
                await CopyAsync(connection, tableName, columns, properties, chunk, ct);
            return;
        }

        // Tabela temporária criada UMA vez e reutilizada com TRUNCATE entre chunks.
        var tmpTable = $"tmp_{tableName.Replace('.', '_')}_{Guid.NewGuid():N}";
        await unitOfWork.ExecuteAsync($"CREATE TEMP TABLE {tmpTable} (LIKE {tableName}) ON COMMIT DROP", null, ct);

        var copyCommand = $"COPY {tmpTable} ({columns}) FROM STDIN (FORMAT BINARY)";
        var mergeSql = $"INSERT INTO {tableName} ({columns}) SELECT {columns} FROM {tmpTable} {onConflictClause}";

        var chunkCount = 0;
        foreach (var chunk in Chunk(list, chunkSize)) {
            await using (var writer = await connection.BeginBinaryImportAsync(copyCommand, ct)) {
                await WriteRowsAsync(writer, properties, chunk, ct);
                await writer.CompleteAsync(ct);
            }

            await unitOfWork.ExecuteAsync(mergeSql, null, ct);
            await unitOfWork.ExecuteAsync($"TRUNCATE TABLE {tmpTable}", null, ct);
            chunkCount++;
        }

        logger.LogDebug("BulkInsertChunked concluído: {Count} linhas em {Chunks} chunks.", list.Count, chunkCount);
    }

    private async Task CopyAsync<TEntity>(NpgsqlConnection connection, string tableName, string columns,
        PropertyInfo[] properties, IReadOnlyList<TEntity> list, CancellationToken ct) where TEntity : class {
        var copyCommand = $"COPY {tableName} ({columns}) FROM STDIN (FORMAT BINARY)";
        logger.LogTrace("BulkInsert via COPY: {Count} linhas.", list.Count);

        await using var writer = await connection.BeginBinaryImportAsync(copyCommand, ct);
        await WriteRowsAsync(writer, properties, list, ct);
        await writer.CompleteAsync(ct);
    }

    private async Task UpsertViaTempTableAsync<TEntity>(NpgsqlConnection connection, IUnitOfWork unitOfWork,
        string tableName, string columns, PropertyInfo[] properties, IReadOnlyList<TEntity> list,
        string onConflictClause, CancellationToken ct) where TEntity : class {
        var tmpTable = $"tmp_{tableName.Replace('.', '_')}_{Guid.NewGuid():N}";
        logger.LogTrace("BulkUpsert via temp table '{TmpTable}': {Count} linhas.", tmpTable, list.Count);

        await unitOfWork.ExecuteAsync($"CREATE TEMP TABLE {tmpTable} (LIKE {tableName}) ON COMMIT DROP", null, ct);

        var copyCommand = $"COPY {tmpTable} ({columns}) FROM STDIN (FORMAT BINARY)";
        await using (var writer = await connection.BeginBinaryImportAsync(copyCommand, ct)) {
            await WriteRowsAsync(writer, properties, list, ct);
            await writer.CompleteAsync(ct);
        }

        await unitOfWork.ExecuteAsync(
            $"INSERT INTO {tableName} ({columns}) SELECT {columns} FROM {tmpTable} {onConflictClause}", null, ct);
    }

    private static async Task WriteRowsAsync<TEntity>(NpgsqlBinaryImporter writer, PropertyInfo[] properties,
        IReadOnlyList<TEntity> list, CancellationToken ct) where TEntity : class {
        foreach (var entity in list) {
            await writer.StartRowAsync(ct);
            foreach (var property in properties) {
                var value = property.GetValue(entity);

                if (value is null) {
                    await writer.WriteNullAsync(ct);
                    continue;
                }

                var effectiveType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (effectiveType.IsEnum) {
                    await writer.WriteAsync(value.ToString()!.ToUpperInvariant(), ct);
                    continue;
                }

                await writer.WriteAsync(value, ct);
            }
        }
    }

    private static string BuildColumns(PropertyInfo[] properties, NamingStrategyResolver namingResolver,
        DatabaseOptions options) =>
        string.Join(", ", properties.Select(p => {
            var context = NamingContext.ForColumn(options.NamingConvention, p.Name);
            return namingResolver.GetStrategy(context).Convert(context);
        }));

    private static NpgsqlConnection ResolveConnection(IUnitOfWork unitOfWork) {
        if (unitOfWork is not UnitOfWork uow)
            throw new InvalidOperationException("A sessão deve ser uma UnitOfWork do provider PostgreSQL.");
        if (uow.GetConnection() is not NpgsqlConnection connection)
            throw new InvalidOperationException("A conexão subjacente não é uma NpgsqlConnection.");
        return connection;
    }

    private static IEnumerable<IReadOnlyList<TEntity>> Chunk<TEntity>(IReadOnlyList<TEntity> source, int size) {
        for (var i = 0; i < source.Count; i += size) {
            var count = Math.Min(size, source.Count - i);
            var chunk = new List<TEntity>(count);
            for (var j = 0; j < count; j++)
                chunk.Add(source[i + j]);
            yield return chunk;
        }
    }
}
