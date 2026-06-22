namespace Aedis.Database.SqlServer.Queries;

/// <summary>Operadores de comparação escalar.</summary>
public enum ComparisonOperator
{
    /// <summary>Igualdade (<c>=</c>).</summary>
    Equals,

    /// <summary>Diferença (<c>&lt;&gt;</c>).</summary>
    NotEquals,

    /// <summary>Maior que (<c>&gt;</c>).</summary>
    GreaterThan,

    /// <summary>Maior ou igual (<c>&gt;=</c>).</summary>
    GreaterThanOrEqual,

    /// <summary>Menor que (<c>&lt;</c>).</summary>
    LessThan,

    /// <summary>Menor ou igual (<c>&lt;=</c>).</summary>
    LessThanOrEqual,

    /// <summary>Correspondência por padrão (<c>LIKE</c>).</summary>
    Like,

    /// <summary>Negação de padrão (<c>NOT LIKE</c>).</summary>
    NotLike,

    /// <summary>Teste de nulo (<c>IS NULL</c>).</summary>
    IsNull,

    /// <summary>Teste de não-nulo (<c>IS NOT NULL</c>).</summary>
    IsNotNull
}

/// <summary>Operador lógico de um grupo de condições.</summary>
public enum LogicalOperator
{
    /// <summary>Une as condições do grupo com <c>AND</c> (todas devem ser verdadeiras).</summary>
    And,

    /// <summary>Une as condições do grupo com <c>OR</c> (ao menos uma deve ser verdadeira).</summary>
    Or
}

/// <summary>Tipo de junção (<c>JOIN</c>) entre a tabela base e uma tabela secundária.</summary>
public enum JoinType
{
    /// <summary>Junção interna (<c>INNER JOIN</c>): só linhas com correspondência em ambos os lados.</summary>
    Inner,

    /// <summary>Junção à esquerda (<c>LEFT JOIN</c>): todas as linhas da esquerda, com nulos à direita sem par.</summary>
    Left,

    /// <summary>Junção à direita (<c>RIGHT JOIN</c>): todas as linhas da direita, com nulos à esquerda sem par.</summary>
    Right,

    /// <summary>Junção cruzada (<c>CROSS JOIN</c>): produto cartesiano, sem condição <c>ON</c>.</summary>
    Cross
}

internal interface IConditionNode
{
    string BuildSql(ref int parameterIndex, Dictionary<string, object> parameters);
}

internal static class ParameterWriter
{
    /// <summary>
    ///     Adiciona o valor como bind parameter e devolve o nome <c>@pN</c>. Enums viram string em
    ///     MAIÚSCULAS, em paridade com o write path (repositório/bulk), para que a comparação no WHERE case
    ///     com o valor persistido.
    /// </summary>
    public static string Add(object? value, ref int index, Dictionary<string, object> parameters) {
        var name = $"p{index++}";
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
