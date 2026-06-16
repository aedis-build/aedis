using System.Collections;
using System.Reflection;
using Aedis.Database.Abstractions;
using Aedis.Database.Postgres.Naming;
using Aedis.Database.Postgres.Queries;
using Aedis.Security.Abstractions;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Database.Postgres;

/// <summary>
///     Repositório PostgreSQL convention-based: mapeia a entidade para tabela/colunas pela
///     <see cref="DatabaseOptions.NamingConvention" /> (padrão snake_case), usa a propriedade <c>Id</c>
///     como chave e detecta soft-delete pela presença de uma propriedade <c>IsDeleted</c>. Leituras vão
///     em sessão somente leitura; escritas em sessão transacional. O <see cref="SaveAsync(TEntity,CancellationToken)" />
///     é um upsert (<c>INSERT … ON CONFLICT (id) DO UPDATE</c>). Bulk insert delega ao
///     <see cref="PostgresBulkInserter" /> (COPY binário). Enums são persistidos como string maiúscula,
///     em paridade com o caminho de bulk.
/// </summary>
public class PostgresRepository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : class
    where TId : notnull
{
    private readonly IAuditContext? _audit;
    private readonly AuditColumns _auditColumns;
    private readonly PostgresBulkInserter _bulkInserter;
    private readonly PropertyInfo[] _columns;
    private readonly bool _hasSoftDelete;
    private readonly PropertyInfo _idProperty;
    private readonly ILogger _logger;
    private readonly NamingStrategyResolver _naming;
    private readonly IUnitOfWorkFactory _sessionFactory;

    protected readonly DatabaseOptions Options;
    protected readonly string TableName;

    public PostgresRepository(IUnitOfWorkFactory sessionFactory, ILogger<PostgresRepository<TEntity, TId>> logger,
        NamingStrategyResolver naming, IOptions<DatabaseOptions> options, PostgresBulkInserter bulkInserter,
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
        _hasSoftDelete = _columns.Any(p => p.Name.Equals("IsDeleted", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Read --------------------------------------------------------------------------------------

    public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default) =>
        InReadSessionAsync((uow, c) => GetByIdAsync(id, uow, c), ct);

    public async Task<TEntity?> GetByIdAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default) {
        var sql = $"SELECT {SelectColumns()} FROM {TableName} WHERE {Col("Id")} = @Id{SoftDeleteSuffix()}";
        return await unitOfWork.QuerySingleOrDefaultAsync<TEntity>(sql, new { Id = id }, ct);
    }

    public Task<IEnumerable<TEntity>> FindAsync(ICriteria<TEntity> criteria, CancellationToken ct = default) =>
        InReadSessionAsync((uow, c) => FindAsync(criteria, uow, c), ct);

    public async Task<IEnumerable<TEntity>> FindAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default) {
        var (sql, parameters) = criteria.Build();
        return await unitOfWork.QueryAsync<TEntity>(sql, parameters, ct);
    }

    public Task<int> CountAsync(ICriteria<TEntity> criteria, CancellationToken ct = default) =>
        InReadSessionAsync((uow, c) => CountAsync(criteria, uow, c), ct);

    public async Task<int> CountAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default) {
        var (sql, parameters) = criteria.Build();
        return await unitOfWork.QuerySingleOrDefaultAsync<int>($"SELECT count(*) FROM ({sql}) AS _c", parameters, ct);
    }

    public Task<bool> ExistsAsync(TId id, CancellationToken ct = default) =>
        InReadSessionAsync((uow, c) => ExistsAsync(id, uow, c), ct);

    public async Task<bool> ExistsAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default) {
        var sql = $"SELECT EXISTS(SELECT 1 FROM {TableName} WHERE {Col("Id")} = @Id{SoftDeleteSuffix()})";
        return await unitOfWork.QuerySingleOrDefaultAsync<bool>(sql, new { Id = id }, ct);
    }

    public Task<IEnumerable<TResult>> QueryAsync<TResult>(ICriteria<TResult> criteria, CancellationToken ct = default) =>
        InReadSessionAsync((uow, c) => QueryAsync(criteria, uow, c), ct);

    public async Task<IEnumerable<TResult>> QueryAsync<TResult>(ICriteria<TResult> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default) {
        var (sql, parameters) = criteria.Build();
        return await unitOfWork.QueryAsync<TResult>(sql, parameters, ct);
    }

    // ---- Write -------------------------------------------------------------------------------------

    public Task<TEntity> SaveAsync(TEntity entity, CancellationToken ct = default) =>
        InWriteSessionAsync((uow, c) => SaveAsync(entity, uow, c), ct);

    public async Task<TEntity> SaveAsync(TEntity entity, IUnitOfWork unitOfWork, CancellationToken ct = default) {
        Stamp(entity);
        var columns = string.Join(", ", _columns.Select(p => Col(p.Name)));
        var values = string.Join(", ", _columns.Select(p => "@" + p.Name));

        // Mesmo template method do bulk: a cláusula de upsert é definida uma vez em GetOnConflictClause()
        // e vale tanto para o Save (INSERT) quanto para BulkInsert/BulkInsertChunked.
        var conflict = GetOnConflictClause();
        var sql = $"INSERT INTO {TableName} ({columns}) VALUES ({values})"
                  + (conflict is null ? string.Empty : " " + conflict);

        await unitOfWork.ExecuteAsync(sql, ToParameters(entity, _columns), ct);
        return entity;
    }

    public Task DeleteAsync(TId id, CancellationToken ct = default) =>
        InWriteSessionAsync(async (uow, c) => {
            await DeleteAsync(id, uow, c);
            return true;
        }, ct);

    public async Task DeleteAsync(TId id, IUnitOfWork unitOfWork, CancellationToken ct = default) {
        var sql = _hasSoftDelete
            ? $"UPDATE {TableName} SET {Col("IsDeleted")} = true WHERE {Col("Id")} = @Id"
            : $"DELETE FROM {TableName} WHERE {Col("Id")} = @Id";
        await unitOfWork.ExecuteAsync(sql, new { Id = id }, ct);
    }

    public Task BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) =>
        InWriteSessionAsync(async (uow, c) => {
            await BulkInsertAsync(entities, uow, c);
            return true;
        }, ct);

    public Task BulkInsertAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork, CancellationToken ct = default) =>
        _bulkInserter.BulkInsertAsync(unitOfWork, TableName, _columns, Stamped(entities), _naming, Options,
            GetOnConflictClause(), ct);

    public Task BulkInsertChunkedAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) =>
        InWriteSessionAsync(async (uow, c) => {
            await BulkInsertChunkedAsync(entities, uow, c);
            return true;
        }, ct);

    public Task BulkInsertChunkedAsync(IEnumerable<TEntity> entities, IUnitOfWork unitOfWork,
        CancellationToken ct = default) =>
        _bulkInserter.BulkInsertChunkedAsync(unitOfWork, TableName, _columns, Stamped(entities), _naming, Options,
            Options.BulkInsertChunkSize, GetOnConflictClause(), ct);

    public Task BulkUpdateAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) =>
        InWriteSessionAsync(async (uow, c) => {
            await BulkUpdateAsync(entities, uow, c);
            return true;
        }, ct);

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

    public Task<int> CommandAsync(ICriteria<TEntity> criteria, CancellationToken ct = default) =>
        InWriteSessionAsync((uow, c) => CommandAsync(criteria, uow, c), ct);

    public async Task<int> CommandAsync(ICriteria<TEntity> criteria, IUnitOfWork unitOfWork,
        CancellationToken ct = default) {
        var (sql, parameters) = criteria.Build();
        return await unitOfWork.ExecuteAsync(sql, parameters, ct);
    }

    /// <summary>
    ///     Template method da cláusula <c>ON CONFLICT</c> — aplicada de forma consistente tanto no
    ///     <see cref="SaveAsync(TEntity,CancellationToken)" /> (INSERT) quanto nas operações de bulk
    ///     (<see cref="BulkInsertAsync(IEnumerable{TEntity},CancellationToken)" /> e
    ///     <see cref="BulkInsertChunkedAsync(IEnumerable{TEntity},CancellationToken)" />). Padrão
    ///     <c>null</c> (insert simples). Sobrescreva para habilitar upsert, ex.:
    ///     <c>protected override string? GetOnConflictClause() => BuildUpsertClause("Id");</c> ou com SQL
    ///     literal: <c>"ON CONFLICT (id) DO UPDATE SET ..."</c>.
    /// </summary>
    protected virtual string? GetOnConflictClause() => null;

    /// <summary>
    ///     Monta uma cláusula de upsert com base nas propriedades de conflito informadas (convertidas para
    ///     colunas pela convenção de nomes): <c>ON CONFLICT (cols) DO UPDATE SET &lt;colunas não-chave&gt; =
    ///     EXCLUDED.…</c>. Quando não há colunas para atualizar, gera <c>DO NOTHING</c>. Os nomes são
    ///     validados como identificadores SQL, evitando injeção mesmo se vierem de origem dinâmica.
    /// </summary>
    protected string BuildUpsertClause(params string[] conflictProperties) {
        var targets = (conflictProperties.Length > 0 ? conflictProperties : ["Id"])
            .Select(p => SqlIdentifier.Validate(Col(p)));
        var sets = _columns.Where(IsNotId).Select(p => $"{Col(p.Name)} = EXCLUDED.{Col(p.Name)}").ToArray();

        var conflictTarget = $"ON CONFLICT ({string.Join(", ", targets)})";
        return sets.Length == 0
            ? $"{conflictTarget} DO NOTHING"
            : $"{conflictTarget} DO UPDATE SET {string.Join(", ", sets)}";
    }

    // ---- Helpers -----------------------------------------------------------------------------------

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
    ///     Carimba as colunas de auditoria presentes (CreatedAt/By, UpdatedAt/By, UpdatedReason) a partir
    ///     do <see cref="IAuditContext" />, quando há um contexto registrado. No-op caso contrário.
    /// </summary>
    private void Stamp(TEntity entity) {
        if (_audit is not null && _auditColumns.HasAny)
            _auditColumns.Stamp(entity, _audit);
    }

    /// <summary>Carimba cada entidade de forma preguiçosa (sem materializar antes do inserter).</summary>
    private IEnumerable<TEntity> Stamped(IEnumerable<TEntity> entities) {
        if (_audit is null || !_auditColumns.HasAny)
            return entities;

        return entities.Select(entity => {
            _auditColumns.Stamp(entity, _audit);
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
        string.Join(", ", _columns.Select(p => {
            var column = Col(p.Name);
            return column.Equals(p.Name, StringComparison.Ordinal) ? column : $"{column} AS \"{p.Name}\"";
        }));

    private string SoftDeleteSuffix() => _hasSoftDelete ? $" AND {Col("IsDeleted")} = false" : string.Empty;

    private bool IsNotId(PropertyInfo property) => property != _idProperty;

    private string Col(string propertyName) => Convert(NamingContext.ForColumn(Options.NamingConvention, propertyName));

    private string Convert(NamingContext context) => _naming.GetStrategy(context).Convert(context);

    private static bool IsCollection(Type type) {
        if (type == typeof(string) || type == typeof(byte[]))
            return false; // text / bytea são colunas escalares
        if (!typeof(IEnumerable).IsAssignableFrom(type))
            return false;

        // Arrays/listas de tipos simples (string[], int[], Guid[]…) são colunas Postgres (text[]/int[]/…);
        // coleções de tipos complexos são navegação de agregado e ficam fora das colunas.
        var element = type.IsArray
            ? type.GetElementType()
            : type.IsGenericType
                ? type.GetGenericArguments().FirstOrDefault()
                : null;
        return element is null || !IsSimple(element);
    }

    private static bool IsSimple(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
               || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
               || type == typeof(DateOnly) || type == typeof(TimeOnly);
    }
}
