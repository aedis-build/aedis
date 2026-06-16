using System.Text;
using Aedis.Database.Abstractions;

namespace Aedis.Database.Postgres.Queries;

/// <summary>
///     Construtor fluente de consultas SQL parametrizadas para PostgreSQL, implementando
///     <see cref="ICriteria{TEntity}" />. <strong>Seguro contra SQL injection por construção</strong>: os
///     <em>valores</em> são sempre enviados como bind parameters; os <em>identificadores</em> (colunas)
///     vêm do código da consulta — para nomes dinâmicos, valide com <see cref="SqlIdentifier" />.
///     <para>
///         Subclasse e componha no construtor — where/and/or, in/between, condições de array (GIN),
///         condições de range (GiST), joins, ordenação e paginação:
///     </para>
///     <example>
///         <code>
///         public sealed class ActiveOrders(Guid customer) : PostgresCriteria&lt;Order&gt;("orders") {
///             {
///                 WhereEquals("customer_id", customer).And().WhereEquals("status", "ACTIVE");
///                 Group(LogicalOperator.Or, g => g.WhereGreaterThan("total", 100).WhereArrayOverlap("tags", new[]{"vip"}));
///                 OrderByDescending("created_at").Page(1, 20);
///             }
///         }
///         </code>
///     </example>
/// </summary>
public abstract class PostgresCriteria<TEntity> : ICriteria<TEntity>
{
    private readonly List<string> _joins = [];
    private readonly List<string> _orderBy = [];
    private readonly ConditionGroup _root = new(LogicalOperator.And);
    private readonly Stack<ConditionGroup> _stack = new();

    private bool _distinct;
    private int? _limit;
    private int? _offset;
    private string? _select;

    protected PostgresCriteria(string tableName, string? tableAlias = null) {
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        TableAlias = tableAlias;
        _stack.Push(_root);
    }

    protected string TableName { get; }
    protected string? TableAlias { get; }

    public bool IsDistinct => _distinct;

    public (string Sql, object Parameters) Build() {
        var parameters = new Dictionary<string, object>();
        var parameterIndex = 0;

        var sql = new StringBuilder();
        sql.Append(_distinct ? "SELECT DISTINCT " : "SELECT ");
        sql.Append(string.IsNullOrWhiteSpace(_select) ? "*" : _select);
        sql.Append(" FROM ").Append(TableAlias is null ? TableName : $"{TableName} {TableAlias}");

        foreach (var join in _joins)
            sql.Append(' ').Append(join);

        if (_root.Children.Count > 0)
            sql.Append(" WHERE ").Append(_root.BuildInner(ref parameterIndex, parameters));

        if (_orderBy.Count > 0)
            sql.Append(" ORDER BY ").Append(string.Join(", ", _orderBy));

        if (_limit.HasValue) sql.Append(" LIMIT ").Append(_limit.Value);
        if (_offset.HasValue) sql.Append(" OFFSET ").Append(_offset.Value);

        return (sql.ToString(), parameters);
    }

    // ---- Projeção -----------------------------------------------------------------------------------

    public PostgresCriteria<TEntity> Select(string columns) {
        _select = columns;
        return this;
    }

    public PostgresCriteria<TEntity> Distinct() {
        _distinct = true;
        return this;
    }

    // ---- Condições escalares ------------------------------------------------------------------------

    public PostgresCriteria<TEntity> Where(string column, ComparisonOperator op, object? value = null) =>
        Add(new SimpleCondition(column, op, value));

    public PostgresCriteria<TEntity> WhereEquals(string column, object value) =>
        Where(column, ComparisonOperator.Equals, value);

    public PostgresCriteria<TEntity> WhereNotEquals(string column, object value) =>
        Where(column, ComparisonOperator.NotEquals, value);

    public PostgresCriteria<TEntity> WhereGreaterThan(string column, object value) =>
        Where(column, ComparisonOperator.GreaterThan, value);

    public PostgresCriteria<TEntity> WhereGreaterThanOrEqual(string column, object value) =>
        Where(column, ComparisonOperator.GreaterThanOrEqual, value);

    public PostgresCriteria<TEntity> WhereLessThan(string column, object value) =>
        Where(column, ComparisonOperator.LessThan, value);

    public PostgresCriteria<TEntity> WhereLessThanOrEqual(string column, object value) =>
        Where(column, ComparisonOperator.LessThanOrEqual, value);

    public PostgresCriteria<TEntity> WhereLike(string column, string pattern) =>
        Where(column, ComparisonOperator.Like, pattern);

    public PostgresCriteria<TEntity> WhereNotLike(string column, string pattern) =>
        Where(column, ComparisonOperator.NotLike, pattern);

    public PostgresCriteria<TEntity> WhereIsNull(string column) =>
        Where(column, ComparisonOperator.IsNull);

    public PostgresCriteria<TEntity> WhereIsNotNull(string column) =>
        Where(column, ComparisonOperator.IsNotNull);

    public PostgresCriteria<TEntity> WhereIn<T>(string column, IEnumerable<T> values) =>
        Add(new InCondition(column, false, values.Cast<object>().ToList()));

    public PostgresCriteria<TEntity> WhereNotIn<T>(string column, IEnumerable<T> values) =>
        Add(new InCondition(column, true, values.Cast<object>().ToList()));

    public PostgresCriteria<TEntity> WhereBetween(string column, object low, object high) =>
        Add(new BetweenCondition(column, low, high));

    // ---- Condições de array (GIN) -------------------------------------------------------------------

    /// <summary><c>coluna @&gt; @valor</c> — o array da coluna contém todos os elementos.</summary>
    public PostgresCriteria<TEntity> WhereArrayContains(string column, object values) =>
        Add(new ArrayCondition(column, ArrayOperator.Contains, values));

    /// <summary><c>coluna &lt;@ @valor</c> — o array da coluna está contido nos elementos.</summary>
    public PostgresCriteria<TEntity> WhereArrayContainedBy(string column, object values) =>
        Add(new ArrayCondition(column, ArrayOperator.ContainedBy, values));

    /// <summary><c>coluna &amp;&amp; @valor</c> — overlap (ao menos um elemento em comum).</summary>
    public PostgresCriteria<TEntity> WhereArrayOverlap(string column, object values) =>
        Add(new ArrayCondition(column, ArrayOperator.Overlap, values));

    /// <summary><c>@valor = ANY(coluna)</c> — o valor escalar pertence ao array da coluna.</summary>
    public PostgresCriteria<TEntity> WhereEqualsAny(string column, object value) =>
        Add(new ArrayCondition(column, ArrayOperator.EqualsAny, value));

    // ---- Condições de range (GiST) ------------------------------------------------------------------

    /// <summary>
    ///     Compara o range <c>[startColumn, endColumn]</c> com <c>[rangeStart, rangeEnd]</c> via operador
    ///     de range (GiST). Inclusividade: <c>"[]"</c> (padrão), <c>"[)"</c>, <c>"()"</c>, <c>"(]"</c>.
    /// </summary>
    public PostgresCriteria<TEntity> WhereRange(string startColumn, string endColumn, RangeOperator op,
        object rangeStart, object rangeEnd, string inclusivity = "[]") =>
        Add(new RangeCondition(startColumn, endColumn, op, rangeStart, rangeEnd, inclusivity));

    public PostgresCriteria<TEntity> WhereRangeOverlaps(string startColumn, string endColumn,
        object rangeStart, object rangeEnd, string inclusivity = "[]") =>
        WhereRange(startColumn, endColumn, RangeOperator.Overlaps, rangeStart, rangeEnd, inclusivity);

    // ---- Lógica e grupos ----------------------------------------------------------------------------

    public PostgresCriteria<TEntity> And() {
        _stack.Peek().Operator = LogicalOperator.And;
        return this;
    }

    public PostgresCriteria<TEntity> Or() {
        _stack.Peek().Operator = LogicalOperator.Or;
        return this;
    }

    /// <summary>
    ///     Compõe um subgrupo parentetizado com o operador informado. Use para misturar AND/OR:
    ///     <c>WhereEquals("a",1).Group(LogicalOperator.Or, g =&gt; g.WhereEquals("b",2).WhereEquals("c",3))</c>
    ///     → <c>a = @0 AND (b = @1 OR c = @2)</c>.
    /// </summary>
    public PostgresCriteria<TEntity> Group(LogicalOperator op, Action<PostgresCriteria<TEntity>> build) {
        var group = new ConditionGroup(op);
        _stack.Peek().Add(group);
        _stack.Push(group);
        build(this);
        _stack.Pop();
        return this;
    }

    // ---- Joins --------------------------------------------------------------------------------------

    public PostgresCriteria<TEntity> InnerJoin(string tableName, string onCondition, string? alias = null) =>
        AddJoin(JoinType.Inner, tableName, onCondition, alias);

    public PostgresCriteria<TEntity> LeftJoin(string tableName, string onCondition, string? alias = null) =>
        AddJoin(JoinType.Left, tableName, onCondition, alias);

    public PostgresCriteria<TEntity> RightJoin(string tableName, string onCondition, string? alias = null) =>
        AddJoin(JoinType.Right, tableName, onCondition, alias);

    public PostgresCriteria<TEntity> CrossJoin(string tableName, string? alias = null) =>
        AddJoin(JoinType.Cross, tableName, null, alias);

    // ---- Ordenação e paginação ----------------------------------------------------------------------

    public PostgresCriteria<TEntity> OrderBy(string column) {
        _orderBy.Add($"{column} ASC");
        return this;
    }

    public PostgresCriteria<TEntity> OrderByDescending(string column) {
        _orderBy.Add($"{column} DESC");
        return this;
    }

    public PostgresCriteria<TEntity> Limit(int count) {
        _limit = count;
        return this;
    }

    public PostgresCriteria<TEntity> Offset(int count) {
        _offset = count;
        return this;
    }

    /// <summary>Paginação 1-based: <c>Page(2, 20)</c> → <c>LIMIT 20 OFFSET 20</c>.</summary>
    public PostgresCriteria<TEntity> Page(int page, int pageSize) {
        _limit = pageSize;
        _offset = (Math.Max(page, 1) - 1) * pageSize;
        return this;
    }

    private PostgresCriteria<TEntity> Add(IConditionNode node) {
        _stack.Peek().Add(node);
        return this;
    }

    private PostgresCriteria<TEntity> AddJoin(JoinType type, string tableName, string? onCondition, string? alias) {
        var keyword = type switch {
            JoinType.Inner => "INNER JOIN",
            JoinType.Left => "LEFT JOIN",
            JoinType.Right => "RIGHT JOIN",
            JoinType.Cross => "CROSS JOIN",
            _ => "INNER JOIN"
        };
        var target = alias is null ? tableName : $"{tableName} {alias}";
        _joins.Add(type == JoinType.Cross ? $"{keyword} {target}" : $"{keyword} {target} ON {onCondition}");
        return this;
    }
}
