using System.Text;
using Aedis.Database.Abstractions;

namespace Aedis.Database.SqlServer.Queries;

/// <summary>
///     Construtor fluente de consultas SQL parametrizadas para SQL Server, implementando
///     <see cref="ICriteria{TEntity}" />. <strong>Seguro contra SQL injection por construção</strong>: os
///     <em>valores</em> são sempre enviados como bind parameters; os <em>identificadores</em> (colunas)
///     vêm do código da consulta — para nomes dinâmicos, valide com <see cref="SqlIdentifier" />.
///     <para>
///         Subclasse e componha no construtor — where/and/or, in/between, joins, ordenação e paginação. A
///         paginação usa <c>OFFSET … ROWS FETCH NEXT … ROWS ONLY</c> (exige um <c>ORDER BY</c>; quando
///         ausente, aplica-se <c>ORDER BY (SELECT NULL)</c>):
///     </para>
///     <example>
///         <code>
///         public sealed class ActiveOrders(Guid customer) : SqlServerCriteria&lt;Order&gt;("orders") {
///             {
///                 WhereEquals("customer_id", customer).And().WhereEquals("status", "ACTIVE");
///                 Group(LogicalOperator.Or, g => g.WhereGreaterThan("total", 100).WhereLike("notes", "%vip%"));
///                 OrderByDescending("created_at").Page(1, 20);
///             }
///         }
///         </code>
///     </example>
/// </summary>
public abstract class SqlServerCriteria<TEntity> : ICriteria<TEntity>
{
    private readonly List<string> _joins = [];
    private readonly List<string> _orderBy = [];
    private readonly ConditionGroup _root = new(LogicalOperator.And);
    private readonly Stack<ConditionGroup> _stack = new();

    private bool _distinct;
    private int? _limit;
    private int? _offset;
    private string? _select;

    /// <summary>
    ///     Inicializa o builder para uma tabela, opcionalmente com um alias usado nas colunas e joins.
    /// </summary>
    /// <param name="tableName">Nome da tabela-alvo do FROM.</param>
    /// <param name="tableAlias">Alias da tabela; quando nulo, as colunas não são prefixadas.</param>
    protected SqlServerCriteria(string tableName, string? tableAlias = null) {
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        TableAlias = tableAlias;
        _stack.Push(_root);
    }

    /// <summary>Nome da tabela-alvo, base do FROM.</summary>
    protected string TableName { get; }

    /// <summary>Alias da tabela; quando presente, prefixa colunas e cláusulas de join.</summary>
    protected string? TableAlias { get; }

    /// <inheritdoc />
    public bool IsDistinct => _distinct;

    /// <inheritdoc />
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

        var paginated = _limit.HasValue || _offset.HasValue;
        if (_orderBy.Count > 0)
            sql.Append(" ORDER BY ").Append(string.Join(", ", _orderBy));
        else if (paginated)
            sql.Append(" ORDER BY (SELECT NULL)");

        if (paginated) {
            sql.Append(" OFFSET ").Append(_offset ?? 0).Append(" ROWS");
            if (_limit.HasValue)
                sql.Append(" FETCH NEXT ").Append(_limit.Value).Append(" ROWS ONLY");
        }

        return (sql.ToString(), parameters);
    }


    /// <summary>Define a lista de colunas projetadas no <c>SELECT</c> (substitui o <c>*</c> padrão).</summary>
    public SqlServerCriteria<TEntity> Select(string columns) {
        _select = columns;
        return this;
    }

    /// <summary>Ativa <c>SELECT DISTINCT</c>, eliminando linhas duplicadas do resultado.</summary>
    public SqlServerCriteria<TEntity> Distinct() {
        _distinct = true;
        return this;
    }


    /// <summary>
    ///     Adiciona ao <c>WHERE</c> uma condição de comparação <c>coluna op valor</c> com o operador
    ///     informado. O valor entra como bind parameter; para <c>IsNull</c>/<c>IsNotNull</c>, omita o valor.
    /// </summary>
    public SqlServerCriteria<TEntity> Where(string column, ComparisonOperator op, object? value = null) =>
        Add(new SimpleCondition(column, op, value));

    /// <summary>Adiciona uma condição de igualdade (<c>coluna = valor</c>) ao <c>WHERE</c>.</summary>
    public SqlServerCriteria<TEntity> WhereEquals(string column, object value) =>
        Where(column, ComparisonOperator.Equals, value);

    /// <summary>Adiciona uma condição de diferença (<c>coluna &lt;&gt; valor</c>) ao <c>WHERE</c>.</summary>
    public SqlServerCriteria<TEntity> WhereNotEquals(string column, object value) =>
        Where(column, ComparisonOperator.NotEquals, value);

    /// <summary>Adiciona uma condição "maior que" (<c>coluna &gt; valor</c>) ao <c>WHERE</c>.</summary>
    public SqlServerCriteria<TEntity> WhereGreaterThan(string column, object value) =>
        Where(column, ComparisonOperator.GreaterThan, value);

    /// <summary>Adiciona uma condição "maior ou igual" (<c>coluna &gt;= valor</c>) ao <c>WHERE</c>.</summary>
    public SqlServerCriteria<TEntity> WhereGreaterThanOrEqual(string column, object value) =>
        Where(column, ComparisonOperator.GreaterThanOrEqual, value);

    /// <summary>Adiciona uma condição "menor que" (<c>coluna &lt; valor</c>) ao <c>WHERE</c>.</summary>
    public SqlServerCriteria<TEntity> WhereLessThan(string column, object value) =>
        Where(column, ComparisonOperator.LessThan, value);

    /// <summary>Adiciona uma condição "menor ou igual" (<c>coluna &lt;= valor</c>) ao <c>WHERE</c>.</summary>
    public SqlServerCriteria<TEntity> WhereLessThanOrEqual(string column, object value) =>
        Where(column, ComparisonOperator.LessThanOrEqual, value);

    /// <summary>
    ///     Adiciona uma condição de correspondência por padrão (<c>coluna LIKE padrão</c>) ao <c>WHERE</c>;
    ///     use <c>%</c> e <c>_</c> como curingas.
    /// </summary>
    public SqlServerCriteria<TEntity> WhereLike(string column, string pattern) =>
        Where(column, ComparisonOperator.Like, pattern);

    /// <summary>Adiciona a negação de um padrão (<c>coluna NOT LIKE padrão</c>) ao <c>WHERE</c>.</summary>
    public SqlServerCriteria<TEntity> WhereNotLike(string column, string pattern) =>
        Where(column, ComparisonOperator.NotLike, pattern);

    /// <summary>Adiciona uma verificação de nulo (<c>coluna IS NULL</c>) ao <c>WHERE</c>.</summary>
    public SqlServerCriteria<TEntity> WhereIsNull(string column) =>
        Where(column, ComparisonOperator.IsNull);

    /// <summary>Adiciona uma verificação de não-nulo (<c>coluna IS NOT NULL</c>) ao <c>WHERE</c>.</summary>
    public SqlServerCriteria<TEntity> WhereIsNotNull(string column) =>
        Where(column, ComparisonOperator.IsNotNull);

    /// <summary>
    ///     Adiciona uma condição de pertencimento (<c>coluna IN (…)</c>) ao <c>WHERE</c>; cada valor entra
    ///     como bind parameter. Lista vazia gera <c>1=0</c> (nenhum resultado).
    /// </summary>
    public SqlServerCriteria<TEntity> WhereIn<T>(string column, IEnumerable<T> values) =>
        Add(new InCondition(column, false, values.Cast<object>().ToList()));

    /// <summary>
    ///     Adiciona uma condição de exclusão (<c>coluna NOT IN (…)</c>) ao <c>WHERE</c>. Lista vazia gera
    ///     <c>1=1</c> (não filtra nada).
    /// </summary>
    public SqlServerCriteria<TEntity> WhereNotIn<T>(string column, IEnumerable<T> values) =>
        Add(new InCondition(column, true, values.Cast<object>().ToList()));

    /// <summary>Adiciona um intervalo fechado (<c>coluna BETWEEN baixo AND alto</c>) ao <c>WHERE</c>.</summary>
    public SqlServerCriteria<TEntity> WhereBetween(string column, object low, object high) =>
        Add(new BetweenCondition(column, low, high));


    /// <summary>Define como <c>AND</c> o operador lógico que une as condições do grupo atual.</summary>
    public SqlServerCriteria<TEntity> And() {
        _stack.Peek().Operator = LogicalOperator.And;
        return this;
    }

    /// <summary>Define como <c>OR</c> o operador lógico que une as condições do grupo atual.</summary>
    public SqlServerCriteria<TEntity> Or() {
        _stack.Peek().Operator = LogicalOperator.Or;
        return this;
    }

    /// <summary>
    ///     Compõe um subgrupo parentetizado com o operador informado. Use para misturar AND/OR:
    ///     <c>WhereEquals("a",1).Group(LogicalOperator.Or, g =&gt; g.WhereEquals("b",2).WhereEquals("c",3))</c>
    ///     → <c>a = @0 AND (b = @1 OR c = @2)</c>.
    /// </summary>
    public SqlServerCriteria<TEntity> Group(LogicalOperator op, Action<SqlServerCriteria<TEntity>> build) {
        var group = new ConditionGroup(op);
        _stack.Peek().Add(group);
        _stack.Push(group);
        build(this);
        _stack.Pop();
        return this;
    }


    /// <summary>Adiciona um <c>INNER JOIN</c> à tabela informada com a condição <c>ON</c> e alias opcional.</summary>
    public SqlServerCriteria<TEntity> InnerJoin(string tableName, string onCondition, string? alias = null) =>
        AddJoin(JoinType.Inner, tableName, onCondition, alias);

    /// <summary>Adiciona um <c>LEFT JOIN</c> à tabela informada com a condição <c>ON</c> e alias opcional.</summary>
    public SqlServerCriteria<TEntity> LeftJoin(string tableName, string onCondition, string? alias = null) =>
        AddJoin(JoinType.Left, tableName, onCondition, alias);

    /// <summary>Adiciona um <c>RIGHT JOIN</c> à tabela informada com a condição <c>ON</c> e alias opcional.</summary>
    public SqlServerCriteria<TEntity> RightJoin(string tableName, string onCondition, string? alias = null) =>
        AddJoin(JoinType.Right, tableName, onCondition, alias);

    /// <summary>Adiciona um <c>CROSS JOIN</c> (produto cartesiano) à tabela informada, com alias opcional.</summary>
    public SqlServerCriteria<TEntity> CrossJoin(string tableName, string? alias = null) =>
        AddJoin(JoinType.Cross, tableName, null, alias);


    /// <summary>Acrescenta a coluna ao <c>ORDER BY</c> em ordem ascendente (<c>ASC</c>).</summary>
    public SqlServerCriteria<TEntity> OrderBy(string column) {
        _orderBy.Add($"{column} ASC");
        return this;
    }

    /// <summary>Acrescenta a coluna ao <c>ORDER BY</c> em ordem descendente (<c>DESC</c>).</summary>
    public SqlServerCriteria<TEntity> OrderByDescending(string column) {
        _orderBy.Add($"{column} DESC");
        return this;
    }

    /// <summary>Define o limite: número máximo de linhas retornadas (via <c>FETCH NEXT n ROWS ONLY</c>).</summary>
    public SqlServerCriteria<TEntity> Limit(int count) {
        _limit = count;
        return this;
    }

    /// <summary>Define o deslocamento: quantidade de linhas iniciais a pular (via <c>OFFSET n ROWS</c>).</summary>
    public SqlServerCriteria<TEntity> Offset(int count) {
        _offset = count;
        return this;
    }

    /// <summary>Paginação 1-based: <c>Page(2, 20)</c> → <c>OFFSET 20 ROWS FETCH NEXT 20 ROWS ONLY</c>.</summary>
    public SqlServerCriteria<TEntity> Page(int page, int pageSize) {
        _limit = pageSize;
        _offset = (Math.Max(page, 1) - 1) * pageSize;
        return this;
    }

    private SqlServerCriteria<TEntity> Add(IConditionNode node) {
        _stack.Peek().Add(node);
        return this;
    }

    private SqlServerCriteria<TEntity> AddJoin(JoinType type, string tableName, string? onCondition, string? alias) {
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
