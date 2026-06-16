namespace Aedis.Database.Postgres.Queries;

/// <summary>Operadores de comparação escalar.</summary>
public enum ComparisonOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Like,
    NotLike,
    IsNull,
    IsNotNull
}

/// <summary>Operador lógico de um grupo de condições.</summary>
public enum LogicalOperator
{
    And,
    Or
}

/// <summary>
///     Operadores de array do PostgreSQL (alta performance com índice <strong>GIN</strong>:
///     <c>CREATE INDEX … USING GIN (coluna)</c>).
/// </summary>
public enum ArrayOperator
{
    /// <summary><c>@&gt;</c> — o array da coluna contém todos os elementos informados.</summary>
    Contains,

    /// <summary><c>&lt;@</c> — o array da coluna está contido nos elementos informados.</summary>
    ContainedBy,

    /// <summary><c>&amp;&amp;</c> — os arrays têm ao menos um elemento em comum (overlap).</summary>
    Overlap,

    /// <summary><c>valor = ANY(coluna)</c> — o valor escalar pertence ao array da coluna.</summary>
    EqualsAny
}

/// <summary>
///     Operadores de range do PostgreSQL (alta performance com índice <strong>GiST</strong>; requer a
///     extensão <c>btree_gist</c>).
/// </summary>
public enum RangeOperator
{
    /// <summary><c>&amp;&amp;</c> — os ranges se sobrepõem.</summary>
    Overlaps,

    /// <summary><c>@&gt;</c> — o range da coluna contém o range informado.</summary>
    Contains,

    /// <summary><c>&lt;@</c> — o range da coluna está contido no range informado.</summary>
    ContainedBy,

    /// <summary><c>-|-</c> — os ranges são adjacentes (se tocam sem sobrepor).</summary>
    Adjacent
}

public enum JoinType
{
    Inner,
    Left,
    Right,
    Cross
}

internal interface IConditionNode
{
    string BuildSql(ref int parameterIndex, Dictionary<string, object> parameters);
}

internal static class ParameterWriter
{
    /// <summary>Adiciona o valor como bind parameter (enums viram string) e devolve o nome <c>@pN</c>.</summary>
    public static string Add(object? value, ref int index, Dictionary<string, object> parameters) {
        var name = $"p{index++}";
        // Enums viram string MAIÚSCULA — paridade com o write path (repositório/COPY) para o WHERE casar.
        parameters[name] = value is Enum e ? e.ToString()!.ToUpperInvariant() : value!;
        return "@" + name;
    }
}

internal sealed class SimpleCondition(string column, ComparisonOperator op, object? value) : IConditionNode
{
    public string BuildSql(ref int parameterIndex, Dictionary<string, object> parameters) {
        var sqlOp = op switch {
            ComparisonOperator.Equals => "=",
            ComparisonOperator.NotEquals => "<>",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanOrEqual => "<=",
            ComparisonOperator.Like => "LIKE",
            ComparisonOperator.NotLike => "NOT LIKE",
            ComparisonOperator.IsNull => "IS NULL",
            ComparisonOperator.IsNotNull => "IS NOT NULL",
            _ => "="
        };

        if (op is ComparisonOperator.IsNull or ComparisonOperator.IsNotNull)
            return $"{column} {sqlOp}";

        var param = ParameterWriter.Add(value, ref parameterIndex, parameters);
        return $"{column} {sqlOp} {param}";
    }
}

internal sealed class InCondition(string column, bool isNotIn, IReadOnlyList<object> values) : IConditionNode
{
    public string BuildSql(ref int parameterIndex, Dictionary<string, object> parameters) {
        if (values.Count == 0) return isNotIn ? "1=1" : "1=0";

        var names = new List<string>(values.Count);
        foreach (var value in values)
            names.Add(ParameterWriter.Add(value, ref parameterIndex, parameters));

        return $"{column} {(isNotIn ? "NOT IN" : "IN")} ({string.Join(", ", names)})";
    }
}

internal sealed class BetweenCondition(string column, object low, object high) : IConditionNode
{
    public string BuildSql(ref int parameterIndex, Dictionary<string, object> parameters) {
        var lo = ParameterWriter.Add(low, ref parameterIndex, parameters);
        var hi = ParameterWriter.Add(high, ref parameterIndex, parameters);
        return $"{column} BETWEEN {lo} AND {hi}";
    }
}

/// <summary>Condição de array (GIN). Npgsql converte <c>string[]</c>/<c>int[]</c> no tipo de array correto.</summary>
internal sealed class ArrayCondition(string column, ArrayOperator op, object value) : IConditionNode
{
    public string BuildSql(ref int parameterIndex, Dictionary<string, object> parameters) {
        var param = ParameterWriter.Add(value, ref parameterIndex, parameters);

        if (op == ArrayOperator.EqualsAny)
            return $"{param} = ANY({column})";

        var sqlOp = op switch {
            ArrayOperator.Contains => "@>",
            ArrayOperator.ContainedBy => "<@",
            ArrayOperator.Overlap => "&&",
            _ => "@>"
        };
        return $"{column} {sqlOp} {param}";
    }
}

/// <summary>Condição de range (GiST) montando <c>tstzrange</c> a partir das colunas de início/fim.</summary>
internal sealed class RangeCondition(
    string startColumn,
    string endColumn,
    RangeOperator op,
    object rangeStart,
    object rangeEnd,
    string inclusivity) : IConditionNode
{
    public string BuildSql(ref int parameterIndex, Dictionary<string, object> parameters) {
        var start = ParameterWriter.Add(rangeStart, ref parameterIndex, parameters);
        var end = ParameterWriter.Add(rangeEnd, ref parameterIndex, parameters);

        var sqlOp = op switch {
            RangeOperator.Overlaps => "&&",
            RangeOperator.Contains => "@>",
            RangeOperator.ContainedBy => "<@",
            RangeOperator.Adjacent => "-|-",
            _ => "&&"
        };

        return $"tstzrange({startColumn}, {endColumn}, '{inclusivity}') {sqlOp} "
               + $"tstzrange({start}, {end}, '{inclusivity}')";
    }
}

internal sealed class ConditionGroup(LogicalOperator op) : IConditionNode
{
    public LogicalOperator Operator { get; set; } = op;
    public List<IConditionNode> Children { get; } = [];

    public void Add(IConditionNode node) => Children.Add(node);

    /// <summary>Como nó filho: parêntese quando há mais de uma condição.</summary>
    public string BuildSql(ref int parameterIndex, Dictionary<string, object> parameters) {
        var inner = BuildInner(ref parameterIndex, parameters);
        return Children.Count > 1 ? $"({inner})" : inner;
    }

    /// <summary>Conteúdo do grupo sem parênteses externos (usado na raiz).</summary>
    public string BuildInner(ref int parameterIndex, Dictionary<string, object> parameters) {
        if (Children.Count == 0) return "1=1";
        var keyword = Operator == LogicalOperator.Or ? "OR" : "AND";
        var parts = new List<string>(Children.Count);
        foreach (var child in Children)
            parts.Add(child.BuildSql(ref parameterIndex, parameters));
        return string.Join($" {keyword} ", parts);
    }
}
