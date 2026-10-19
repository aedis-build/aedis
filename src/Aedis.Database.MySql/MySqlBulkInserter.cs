using System.Reflection;
using System.Text;
using Aedis.Database.Abstractions;
using Aedis.Database.Naming;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Aedis.Database.MySql;

/// <summary>
///     Motor de bulk insert/upsert do MySQL via <c>INSERT</c> multi-linha parametrizado, em lotes de
///     <see cref="DatabaseOptions.BulkInsertChunkSize" /> linhas (um parâmetro nomeado por célula — nada de
///     <c>LOAD DATA LOCAL</c>, que exige habilitar leitura de arquivos do cliente no servidor). Três modos:
///     <list type="bullet">
///         <item>insert direto (sem spec de upsert);</item>
///         <item>upsert sem guarda: o mesmo <c>INSERT</c> multi-linha com <c>AS new ON DUPLICATE KEY UPDATE</c>;</item>
///         <item>upsert com guarda: staging em <c>CREATE TEMPORARY TABLE … LIKE</c>, reutilizada entre os
///         lotes com <c>DELETE</c> (um <c>TRUNCATE</c> faria commit implícito e quebraria a transação), e a
///         cada lote <c>UPDATE … JOIN staging … WHERE guarda</c> seguido de <c>INSERT … SELECT</c> das
///         linhas ausentes.</item>
///     </list>
///     Enums são gravados pelo nome declarado (<see cref="EnumPersistence" />), em paridade com o INSERT do
///     repositório; <see cref="DateOnly" />, <see cref="TimeOnly" />, <see cref="DateTimeOffset" /> e
///     dicionários JSON passam pelos type handlers registrados no Dapper.
/// </summary>
public sealed class MySqlBulkInserter(ILogger<MySqlBulkInserter> logger)
{
    /// <summary>
    ///     Insere (ou faz upsert, se <paramref name="upsert" /> for informado) todas as entidades em lotes de
    ///     <see cref="DatabaseOptions.BulkInsertChunkSize" />; equivale a
    ///     <see cref="BulkInsertChunkedAsync{TEntity}" /> com o tamanho de lote das opções.
    /// </summary>
    public Task BulkInsertAsync<TEntity>(IUnitOfWork unitOfWork, string tableName, PropertyInfo[] properties,
        IEnumerable<TEntity> entities, NamingStrategyResolver namingResolver, DatabaseOptions options,
        UpsertSpec? upsert = null, CancellationToken ct = default) where TEntity : class =>
        BulkInsertChunkedAsync(unitOfWork, tableName, properties, entities, namingResolver, options,
            options.BulkInsertChunkSize, upsert, ct);

    /// <summary>
    ///     Insere (ou faz upsert) em lotes de <paramref name="chunkSize" /> linhas. Com spec guardada, cria
    ///     a tabela temporária de staging <em>uma única vez</em> e a esvazia com <c>DELETE</c> entre os lotes.
    /// </summary>
    public async Task BulkInsertChunkedAsync<TEntity>(IUnitOfWork unitOfWork, string tableName,
        PropertyInfo[] properties, IEnumerable<TEntity> entities, NamingStrategyResolver namingResolver,
        DatabaseOptions options, int chunkSize, UpsertSpec? upsert = null, CancellationToken ct = default)
        where TEntity : class {
        var list = entities as IReadOnlyList<TEntity> ?? entities.ToList();
        if (list.Count == 0) return;

        string Column(string property) {
            var context = NamingContext.ForColumn(options.NamingConvention, property);
            return namingResolver.GetStrategy(context).Convert(context);
        }

        var propertyNames = properties.Select(p => p.Name).ToList();
        var columns = string.Join(", ", propertyNames.Select(Column));
        var staging = $"aedis_stg_{Guid.NewGuid():N}"[..22];
        var guardedUpdate = upsert is null
            ? null
            : MySqlUpsertCompiler.GuardedUpdate(upsert, tableName, propertyNames, Column, p => "s." + Column(p), staging);
        var onDuplicate = upsert is null
            ? string.Empty
            : " " + MySqlUpsertCompiler.OnDuplicateKeyUpdate(upsert, tableName, propertyNames, Column);

        if (guardedUpdate is null) {
            var suffix = upsert is null ? string.Empty : " AS new" + onDuplicate;
            foreach (var chunk in Chunk(list, chunkSize))
                await InsertRowsAsync(unitOfWork, tableName, columns, properties, chunk, suffix, ct);
            logger.LogDebug("BulkInsert concluído: {Count} linhas em {Table}.", list.Count, tableName);
            return;
        }

        await unitOfWork.ExecuteAsync($"CREATE TEMPORARY TABLE {staging} LIKE {tableName}", null, ct);
        var insertMissing = $"INSERT INTO {tableName} ({columns}) SELECT {columns} FROM {staging}{onDuplicate}";

        var chunkCount = 0;
        foreach (var chunk in Chunk(list, chunkSize)) {
            await InsertRowsAsync(unitOfWork, staging, columns, properties, chunk, string.Empty, ct);
            await unitOfWork.ExecuteAsync(guardedUpdate, null, ct);
            await unitOfWork.ExecuteAsync(insertMissing, null, ct);
            await unitOfWork.ExecuteAsync($"DELETE FROM {staging}", null, ct);
            chunkCount++;
        }

        await unitOfWork.ExecuteAsync($"DROP TEMPORARY TABLE {staging}", null, ct);
        logger.LogDebug("BulkUpsert guardado concluído: {Count} linhas em {Chunks} lotes.", list.Count, chunkCount);
    }

    private static Task<int> InsertRowsAsync<TEntity>(IUnitOfWork unitOfWork, string tableName, string columns,
        PropertyInfo[] properties, IReadOnlyList<TEntity> rows, string suffix, CancellationToken ct) where TEntity : class {
        var sql = new StringBuilder().Append("INSERT INTO ").Append(tableName).Append(" (").Append(columns).Append(") VALUES ");
        var parameters = new DynamicParameters();

        for (var row = 0; row < rows.Count; row++) {
            sql.Append(row == 0 ? "(" : ", (");
            for (var cell = 0; cell < properties.Length; cell++) {
                var name = $"@r{row}_{cell}";
                sql.Append(cell == 0 ? name : ", " + name);
                parameters.Add(name, EnumPersistence.ToStored(properties[cell].GetValue(rows[row])));
            }

            sql.Append(')');
        }

        return unitOfWork.ExecuteAsync(sql.Append(suffix).ToString(), parameters, ct);
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
