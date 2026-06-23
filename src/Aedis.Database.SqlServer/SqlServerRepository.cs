using System.Collections;
using System.Reflection;
using Aedis.Database.Abstractions;
using Aedis.Database.SqlServer.Naming;
using Aedis.Security.Abstractions;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Database.SqlServer;

/// <summary>
///     Repositório SQL Server convention-based: mapeia a entidade para tabela/colunas pela
///     <see cref="DatabaseOptions.NamingConvention" /> (padrão snake_case), usa a propriedade <c>Id</c>
///     como chave e detecta soft-delete pela presença de uma propriedade <c>IsDeleted</c>. Leituras vão
///     em sessão somente leitura; escritas em sessão transacional. O
///     <see cref="SaveAsync(TEntity,CancellationToken)" /> é um upsert via <c>MERGE</c> quando há chave de
///     conflito declarada. Bulk insert delega ao <see cref="SqlServerBulkInserter" /> (SqlBulkCopy em
///     streaming + MERGE). Enums são persistidos como string maiúscula, em paridade com o caminho de bulk.
/// </summary>
public class SqlServerRepository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : class
    where TId : notnull
{
    private readonly IAuditContext? _audit;
    private readonly AuditColumns _auditColumns;
    private readonly SqlServerBulkInserter _bulkInserter;
    private readonly PropertyInfo[] _columns;
    private readonly bool _hasDeletedAt;
    private readonly bool _hasDeletedBy;
    private readonly bool _hasSoftDelete;
    private readonly PropertyInfo _idProperty;
    private readonly ILogger _logger;
    private readonly NamingStrategyResolver _naming;
    private readonly IUnitOfWorkFactory _sessionFactory;

    /// <summary>Opções do provider SQL Server em vigor para este repositório (convenção de nomes, pool, auditoria, chunk de bulk).</summary>
    protected readonly DatabaseOptions Options;

    /// <summary>Nome da tabela resolvido para a entidade — vindo do parâmetro explícito ou da convenção de nomes aplicada ao nome do tipo.</summary>
    protected readonly string TableName;

    /// <summary>
    ///     Constrói o repositório, resolvendo tabela e colunas da entidade por reflexão e convenção de
    ///     nomes, localizando a chave <c>Id</c> e detectando as colunas de soft-delete (IsDeleted/DeletedAt/DeletedBy).
    /// </summary>
    /// <param name="sessionFactory">Fábrica de sessões (escrita/leitura) usada pelos métodos sem unidade de trabalho explícita.</param>
    /// <param name="logger">Logger do repositório.</param>
    /// <param name="naming">Resolvedor de estratégias de nomes para mapear propriedades em colunas/tabelas.</param>
    /// <param name="options">Opções do provider SQL Server.</param>
    /// <param name="bulkInserter">Inseridor em massa via SqlBulkCopy em streaming, usado pelas operações de bulk.</param>
    /// <param name="tableName">Nome de tabela explícito; quando <c>null</c>, deriva-se do nome da entidade pela convenção.</param>
    /// <param name="auditContext">Contexto de auditoria opcional; quando presente, carimba as colunas de auditoria existentes.</param>
    public SqlServerRepository(IUnitOfWorkFactory sessionFactory, ILogger<SqlServerRepository<TEntity, TId>> logger,
        NamingStrategyResolver naming, IOptions<DatabaseOptions> options, SqlServerBulkInserter bulkInserter,
        string? tableName = null, IAuditContext? auditContext = null) {
        _sessionFactory = sessionFactory;
        _logger = logger;
        _naming = naming;
        Options = options.Value;
        _bulkInserter = bulkInserter;
        _audit = auditContext;
        _auditColumns = AuditColumns.For(typeof(TEntity));

        var entityType = typeof(TEntity);
        TableName = tableName ?? Convert(NamingContext.ForTable(Options.NamingConvention, entityType.Name));
        _columns = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true } && !IsCollection(p.PropertyType))
            .ToArray();
        _idProperty = _columns.FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidOperationException($"A entidade {entityType.Name} não tem propriedade Id.");
        _hasSoftDelete = HasColumn("IsDeleted");
        _hasDeletedAt = HasColumn("DeletedAt");
        _hasDeletedBy = HasColumn("DeletedBy");
    }

    private bool HasColumn(string name) =>
        _columns.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));


    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default) =>
        InReadSessionAsync((uow, c) => GetByIdAsync(id, uow, c), ct);

    /// <inheritdoc />
    public async Task<TEntity?> GetByIdAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default) {
        var sql = $"SELECT {SelectColumns()} FROM {TableName} WHERE {Col("Id")} = @Id{SoftDeleteSuffix()}";
        return await unitOfWork.QuerySingleOrDefaultAsync<TEntity>(sql, new { Id = id }, ct);
    }

    /// <inheritdoc />
    public Task<IEnumerable<TEntity>> FindAsync(ICriteria<TEntity> criteria, CancellationToken ct = default) =>
        InReadSessionAsync((uow, c) => FindAsync(criteria, uow, c), ct);

    /// <inheritdoc />
    public async Task<IEnumerable<TEntity>> FindAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default) {
        var (sql, parameters) = criteria.Build();
        return await unitOfWork.QueryAsync<TEntity>(sql, parameters, ct);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(ICriteria<TEntity> criteria, CancellationToken ct = default) =>
        InReadSessionAsync((uow, c) => CountAsync(criteria, uow, c), ct);

    /// <inheritdoc />
    public async Task<int> CountAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default) {
        var (sql, parameters) = criteria.Build();
        return await unitOfWork.QuerySingleOrDefaultAsync<int>($"SELECT count(*) FROM ({sql}) AS _c", parameters, ct);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(TId id, CancellationToken ct = default) =>
        InReadSessionAsync((uow, c) => ExistsAsync(id, uow, c), ct);

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default) {
        var sql = $"SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM {TableName} WHERE {Col("Id")} = @Id"
                  + $"{SoftDeleteSuffix()}) THEN 1 ELSE 0 END AS BIT)";
        return await unitOfWork.QuerySingleOrDefaultAsync<bool>(sql, new { Id = id }, ct);
    }

    /// <inheritdoc />
    public Task<IEnumerable<TResult>> QueryAsync<TResult>(ICriteria<TResult> criteria, CancellationToken ct = default) =>
        InReadSessionAsync((uow, c) => QueryAsync(criteria, uow, c), ct);

    /// <inheritdoc />
    public async Task<IEnumerable<TResult>> QueryAsync<TResult>(ICriteria<TResult> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default) {
        var (sql, parameters) = criteria.Build();
        return await unitOfWork.QueryAsync<TResult>(sql, parameters, ct);
    }


    /// <inheritdoc />
    public Task<TEntity> SaveAsync(TEntity entity, CancellationToken ct = default) =>
        InWriteSessionAsync((uow, c) => SaveAsync(entity, uow, c), ct);

    /// <inheritdoc />
    public async Task<TEntity> SaveAsync(TEntity entity, IUnitOfWork unitOfWork, CancellationToken ct = default) {
        Stamp(entity);
        var spec = GetUpsertSpec();
        string sql;
        if (spec is null) {
            var columns = string.Join(", ", _columns.Select(p => Col(p.Name)));
            var values = string.Join(", ", _columns.Select(p => "@" + p.Name));
            sql = $"INSERT INTO {TableName} ({columns}) VALUES ({values})";
        }
        else {
            sql = BuildMergeUpsert(spec);
        }

        await unitOfWork.ExecuteAsync(sql, ToParameters(entity, _columns), ct);
        return entity;
    }

    /// <inheritdoc />
    public Task DeleteAsync(TId id, CancellationToken ct = default) =>
        InWriteSessionAsync(async (uow, c) => {
            await DeleteAsync(id, uow, c);
            return true;
        }, ct);

    /// <inheritdoc />
    public async Task DeleteAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default) {
        if (!_hasSoftDelete) {
            await unitOfWork.ExecuteAsync($"DELETE FROM {TableName} WHERE {Col("Id")} = @Id", new { Id = id }, ct);
            return;
        }

        var sets = new List<string> { $"{Col("IsDeleted")} = 1" };
        var parameters = new DynamicParameters();
        parameters.Add("@Id", id);

        if (_audit is not null) {
            if (_hasDeletedAt) {
                sets.Add($"{Col("DeletedAt")} = @DeletedAt");
                parameters.Add("@DeletedAt", _audit.Now);
            }

            if (_hasDeletedBy) {
                sets.Add($"{Col("DeletedBy")} = @DeletedBy");
                parameters.Add("@DeletedBy", _audit.CurrentActor ?? Options.DefaultAuditActor);
            }
        }

        await unitOfWork.ExecuteAsync(
            $"UPDATE {TableName} SET {string.Join(", ", sets)} WHERE {Col("Id")} = @Id", parameters, ct);
    }

    /// <inheritdoc />
    public Task BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) =>
        InWriteSessionAsync(async (uow, c) => {
            await BulkInsertAsync(entities, uow, c);
            return true;
        }, ct);

    /// <inheritdoc />
    public Task BulkInsertAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork, CancellationToken ct = default) =>
        _bulkInserter.BulkInsertAsync(unitOfWork, TableName, _columns, Stamped(entities), _naming, Options,
            GetUpsertSpec(), ct);

    /// <inheritdoc />
    public Task BulkInsertChunkedAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) =>
        InWriteSessionAsync(async (uow, c) => {
            await BulkInsertChunkedAsync(entities, uow, c);
            return true;
        }, ct);

    /// <inheritdoc />
    public Task BulkInsertChunkedAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork,
        CancellationToken ct = default) =>
        _bulkInserter.BulkInsertChunkedAsync(unitOfWork, TableName, _columns, Stamped(entities), _naming, Options,
            Options.BulkInsertChunkSize, GetUpsertSpec(), ct);

    /// <inheritdoc />
    public Task BulkUpdateAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) =>
        InWriteSessionAsync(async (uow, c) => {
            await BulkUpdateAsync(entities, uow, c);
            return true;
        }, ct);

    /// <inheritdoc />
    public async Task BulkUpdateAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork,
        CancellationToken ct = default) {
        var updateColumns = _columns.Where(IsNotId).ToArray();
        var setClause = string.Join(", ", updateColumns.Select(p => $"{Col(p.Name)} = @{p.Name}"));
        var sql = $"UPDATE {TableName} SET {setClause} WHERE {Col("Id")} = @{_idProperty.Name}";

        foreach (var entity in entities) {
            Stamp(entity);
            await unitOfWork.ExecuteAsync(sql, ToParameters(entity, [.. updateColumns, _idProperty]), ct);
        }
    }

    /// <inheritdoc />
    public Task<int> CommandAsync(ICriteria<TEntity> criteria, CancellationToken ct = default) =>
        InWriteSessionAsync((uow, c) => CommandAsync(criteria, uow, c), ct);

    /// <inheritdoc />
    public async Task<int> CommandAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default) {
        var (sql, parameters) = criteria.Build();
        return await unitOfWork.ExecuteAsync(sql, parameters, ct);
    }

    /// <summary>
    ///     Template method da especificação de upsert <strong>agnóstica de provider</strong> — aplicada de
    ///     forma consistente tanto no <see cref="SaveAsync(TEntity,CancellationToken)" /> (MERGE single-row)
    ///     quanto nas operações de bulk (via MERGE sobre staging). Padrão <c>null</c> (insert simples).
    ///     Sobrescreva com uma <see cref="UpsertSpec" /> — a mesma que vale no provider PostgreSQL, sem
    ///     refatoração ao trocar de banco, ex.:
    ///     <c>protected override UpsertSpec? GetUpsertSpec() => UpsertSpec.OnKey("Id");</c> ou, com guarda
    ///     condicional, <c>… .SetServerUtcNow("UpdatedAt").When(g => g.Newer("ObservedAt").AndNotDeleted());</c>.
    /// </summary>
    protected virtual UpsertSpec? GetUpsertSpec() => null;


    private async Task<T> InReadSessionAsync<T>(Func<IUnitOfWork, CancellationToken, Task<T>> action,
        CancellationToken ct) {
        await using var uow = await _sessionFactory.CreateReadSessionAsync(ct);
        try {
            var result = await action(uow, ct);
            await uow.CommitAsync(ct);
            return result;
        }
        catch {
            await uow.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<T> InWriteSessionAsync<T>(Func<IUnitOfWork, CancellationToken, Task<T>> action,
        CancellationToken ct) {
        await using var uow = await _sessionFactory.CreateWriteSessionAsync(ct);
        try {
            var result = await action(uow, ct);
            await uow.CommitAsync(ct);
            return result;
        }
        catch {
            await uow.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    ///     Monta um <c>MERGE</c> single-row (fonte <c>VALUES</c> parametrizada) para o upsert do
    ///     <see cref="SaveAsync(TEntity,CancellationToken)" />, com <c>HOLDLOCK</c> para serializar a
    ///     verificação-e-inserção e evitar corrida sob concorrência. As cláusulas <c>WHEN MATCHED</c>/
    ///     <c>WHEN NOT MATCHED</c> (incluindo a guarda condicional) vêm do <see cref="SqlServerUpsertCompiler" />,
    ///     a mesma fonte usada no caminho de bulk — garantindo Save e bulk idênticos.
    /// </summary>
    private string BuildMergeUpsert(UpsertSpec spec) {
        var allColumns = _columns.Select(p => RawCol(p.Name)).ToList();
        var keyColumns = spec.KeyProperties.Select(RawCol).ToList();

        var sourceColumns = string.Join(", ", allColumns.Select(column => $"[{column}]"));
        var sourceValues = string.Join(", ", _columns.Select(p => "@" + p.Name));
        var on = string.Join(" AND ", keyColumns.Select(key => $"t.[{key}] = s.[{key}]"));
        var tail = SqlServerUpsertCompiler.BuildMergeTail(spec, allColumns, keyColumns, RawCol);

        return $"MERGE INTO {TableName} WITH (HOLDLOCK) AS t USING (VALUES ({sourceValues})) AS s ({sourceColumns}) ON {on} {tail}";
    }

    /// <summary>
    ///     Carimba as colunas de auditoria presentes (CreatedAt/By, UpdatedAt/By, UpdatedReason) a partir
    ///     do <see cref="IAuditContext" />, quando há um contexto registrado. No-op caso contrário.
    /// </summary>
    private void Stamp(TEntity entity) {
        if (_audit is not null && _auditColumns.HasAny)
            _auditColumns.Stamp(entity, _audit, Options.DefaultAuditActor);
    }

    /// <summary>Carimba cada entidade de forma preguiçosa (sem materializar antes do inserter).</summary>
    private IEnumerable<TEntity> Stamped(IEnumerable<TEntity> entities) {
        if (_audit is null || !_auditColumns.HasAny)
            return entities;

        return entities.Select(entity => {
            _auditColumns.Stamp(entity, _audit, Options.DefaultAuditActor);
            return entity;
        });
    }

    private static DynamicParameters ToParameters(TEntity entity, PropertyInfo[] properties) {
        var parameters = new DynamicParameters();
        foreach (var property in properties) {
            var value = property.GetValue(entity);
            if (value is not null) {
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (type.IsEnum) value = value.ToString()!.ToUpperInvariant();
            }

            parameters.Add("@" + property.Name, value);
        }

        return parameters;
    }

    private string SelectColumns() =>
        string.Join(", ", _columns.Select(p => $"{Col(p.Name)} AS [{p.Name}]"));

    private string SoftDeleteSuffix() => _hasSoftDelete ? $" AND {Col("IsDeleted")} = 0" : string.Empty;

    private bool IsNotId(PropertyInfo property) => property != _idProperty;

    private string RawCol(string propertyName) => Convert(NamingContext.ForColumn(Options.NamingConvention, propertyName));

    private string Col(string propertyName) => $"[{RawCol(propertyName)}]";

    private string Convert(NamingContext context) => _naming.GetStrategy(context).Convert(context);

    /// <summary>
    ///     Decide se o tipo deve ficar fora das colunas mapeadas. <c>string</c> e <c>byte[]</c> são colunas
    ///     escalares (nvarchar/varbinary). Qualquer outra coleção (<c>IEnumerable</c>) representa navegação
    ///     de agregado e é excluída — o SQL Server não tem tipo de coluna array nativo, ao contrário do
    ///     PostgreSQL.
    /// </summary>
    private static bool IsCollection(Type type) {
        if (type == typeof(string) || type == typeof(byte[]))
            return false;
        return typeof(IEnumerable).IsAssignableFrom(type);
    }
}
