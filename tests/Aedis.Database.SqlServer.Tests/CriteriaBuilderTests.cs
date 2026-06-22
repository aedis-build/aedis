using Aedis.Database.SqlServer.Queries;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>
///     Builder fluente <see cref="SqlServerCriteria{TEntity}" /> (determinístico, sem banco): montagem de
///     where/and/or, grupos parentetizados, in/between, joins, ordenação e paginação T-SQL
///     (<c>OFFSET … ROWS FETCH NEXT … ROWS ONLY</c>) — com valores sempre parametrizados.
/// </summary>
public sealed class CriteriaBuilderTests
{
    private sealed record Row;

    private enum Status { Pending }

    [Fact]
    public void Monta_where_grupo_in_join_e_paginacao() {
        var (sql, parameters) = new SampleCriteria().Build();
        var p = (IDictionary<string, object>)parameters;

        sql.Should().StartWith("SELECT o.id, o.total FROM orders o");
        sql.Should().Contain("INNER JOIN customers c ON c.id = o.customer_id");
        sql.Should().Contain("status = @p0");
        sql.Should().Contain("total >= @p1");
        sql.Should().Contain("(description LIKE @p2 OR note IS NULL)");
        sql.Should().Contain("region IN (@p3, @p4)");
        sql.Should().Contain("ORDER BY created_at DESC");
        sql.Should().EndWith("OFFSET 20 ROWS FETCH NEXT 20 ROWS ONLY");

        p["p0"].Should().Be("PENDING", "enums viram string");
        p["p3"].Should().Be("sa");
        p["p4"].Should().Be("na");
    }

    [Fact]
    public void Paginacao_sem_order_by_aplica_order_by_select_null() {
        var (sql, _) = new UnorderedPageCriteria().Build();

        sql.Should().Contain("ORDER BY (SELECT NULL)");
        sql.Should().EndWith("OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY");
    }

    [Fact]
    public void WhereBetween_parametriza_limites() {
        var (sql, parameters) = new RangeCriteria().Build();
        var p = (IDictionary<string, object>)parameters;

        sql.Should().Contain("amount BETWEEN @p0 AND @p1");
        p["p0"].Should().Be(10);
        p["p1"].Should().Be(100);
    }

    [Fact]
    public void Distinct_e_select_explicito() {
        var (sql, _) = new DistinctCriteria().Build();

        sql.Should().StartWith("SELECT DISTINCT region FROM orders");
    }

    private sealed class SampleCriteria : SqlServerCriteria<Row>
    {
        public SampleCriteria() : base("orders", "o") {
            Select("o.id, o.total")
                .WhereEquals("status", Status.Pending)
                .And().WhereGreaterThanOrEqual("total", 100m)
                .Group(LogicalOperator.Or, g => g.WhereLike("description", "%a%").WhereIsNull("note"))
                .WhereIn("region", new[] { "sa", "na" })
                .InnerJoin("customers", "c.id = o.customer_id", "c")
                .OrderByDescending("created_at")
                .Page(2, 20);
        }
    }

    private sealed class UnorderedPageCriteria : SqlServerCriteria<Row>
    {
        public UnorderedPageCriteria() : base("orders") => WhereEquals("status", Status.Pending).Page(1, 10);
    }

    private sealed class RangeCriteria : SqlServerCriteria<Row>
    {
        public RangeCriteria() : base("bookings") => WhereBetween("amount", 10, 100);
    }

    private sealed class DistinctCriteria : SqlServerCriteria<Row>
    {
        public DistinctCriteria() : base("orders") => Select("region").Distinct();
    }
}
