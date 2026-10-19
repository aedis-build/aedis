using Aedis.Database.MySql.Queries;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.MySql.Tests;

/// <summary>Operadores de comparação, OR explícito, joins não internos, listas vazias e Limit/Offset avulsos do builder.</summary>
public sealed class CriteriaOperatorsTests
{
    private sealed record Row;

    private sealed class OperatorsCriteria : MySqlCriteria<Row>
    {
        public OperatorsCriteria() : base("orders", "o") {
            WhereNotEquals("status", "x")
                .Or().WhereGreaterThan("total", 1)
                .WhereLessThan("total", 9)
                .WhereLessThanOrEqual("qty", 3)
                .WhereNotLike("name", "%a%")
                .WhereIsNotNull("note")
                .WhereNotIn("region", new[] { "sa" })
                .WhereJsonValue("metadata", "$.flag", ComparisonOperator.IsNotNull)
                .LeftJoin("customers", "c.id = o.customer_id", "c")
                .RightJoin("stores", "s.id = o.store_id")
                .CrossJoin("calendar", "cal")
                .OrderBy("created_at")
                .Limit(3)
                .Offset(5);
        }
    }

    private sealed class EmptyListsCriteria : MySqlCriteria<Row>
    {
        public EmptyListsCriteria() : base("orders") => WhereIn("a", Array.Empty<int>()).WhereNotIn("b", Array.Empty<int>()).Limit(1);
    }

    [Fact]
    public void Monta_todos_os_operadores_joins_e_paginacao_avulsa() {
        var (sql, parameters) = new OperatorsCriteria().Build();
        var p = (IDictionary<string, object>)parameters;

        sql.Should().Contain("status <> @p0 OR total > @p1 OR total < @p2 OR qty <= @p3 OR name NOT LIKE @p4 OR note IS NOT NULL OR region NOT IN (@p5) OR JSON_UNQUOTE(JSON_EXTRACT(metadata, @p6)) IS NOT NULL");
        sql.Should().Contain("LEFT JOIN customers c ON c.id = o.customer_id");
        sql.Should().Contain("RIGHT JOIN stores ON s.id = o.store_id");
        sql.Should().Contain("CROSS JOIN calendar cal");
        sql.Should().Contain("ORDER BY created_at ASC");
        sql.Should().EndWith("LIMIT 3 OFFSET 5");
        p["p5"].Should().Be("sa");
        p["p6"].Should().Be("$.flag");
    }

    [Fact]
    public void Listas_vazias_viram_predicados_constantes_e_Limit_sem_Offset() {
        var (sql, _) = new EmptyListsCriteria().Build();

        sql.Should().Be("SELECT * FROM orders WHERE 1=0 AND 1=1 LIMIT 1");
    }
}
