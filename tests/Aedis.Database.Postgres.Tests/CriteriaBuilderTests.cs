using Aedis.Database.Postgres.Queries;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.Postgres.Tests;

/// <summary>
///     Builder fluente <see cref="PostgresCriteria{TEntity}" /> (determinístico, sem banco): montagem de
///     where/and/or, grupos parentetizados, in/between, condições de array (GIN) e range (GiST), joins,
///     ordenação e paginação — com valores sempre parametrizados.
/// </summary>
public sealed class CriteriaBuilderTests
{
    private sealed record Row;

    private enum Status { Pending }

    [Fact]
    public void Monta_where_grupo_in_array_join_e_paginacao() {
        var (sql, parameters) = new SampleCriteria().Build();
        var p = (IDictionary<string, object>)parameters;

        sql.Should().StartWith("SELECT o.id, o.total FROM orders o");
        sql.Should().Contain("INNER JOIN customers c ON c.id = o.customer_id");
        sql.Should().Contain("status = @p0");
        sql.Should().Contain("total >= @p1");
        sql.Should().Contain("(description LIKE @p2 OR note IS NULL)");
        sql.Should().Contain("region IN (@p3, @p4)");
        sql.Should().Contain("tags && @p5");
        sql.Should().Contain("ORDER BY created_at DESC");
        sql.Should().EndWith("LIMIT 20 OFFSET 20");

        p["p0"].Should().Be("PENDING", "enums viram string");
        p["p3"].Should().Be("sa");
        p["p4"].Should().Be("na");
    }

    [Fact]
    public void WhereEqualsAny_inverte_para_valor_ANY_coluna() {
        var (sql, parameters) = new EqualsAnyCriteria().Build();

        sql.Should().Contain("@p0 = ANY(tags)");
        ((IDictionary<string, object>)parameters)["p0"].Should().Be("vip");
    }

    [Fact]
    public void WhereBetween_e_range_GiST() {
        var (sql, _) = new RangeCriteria().Build();

        sql.Should().Contain("amount BETWEEN @p0 AND @p1");
        sql.Should().Contain("tstzrange(starts_at, ends_at, '[]') && tstzrange(@p2, @p3, '[]')");
    }

    [Fact]
    public void Distinct_e_select_explicito() {
        var (sql, _) = new DistinctCriteria().Build();

        sql.Should().StartWith("SELECT DISTINCT region FROM orders");
    }

    private sealed class SampleCriteria : PostgresCriteria<Row>
    {
        public SampleCriteria() : base("orders", "o") {
            Select("o.id, o.total")
                .WhereEquals("status", Status.Pending)
                .And().WhereGreaterThanOrEqual("total", 100m)
                .Group(LogicalOperator.Or, g => g.WhereLike("description", "%a%").WhereIsNull("note"))
                .WhereIn("region", new[] { "sa", "na" })
                .WhereArrayOverlap("tags", new[] { "vip", "gold" })
                .InnerJoin("customers", "c.id = o.customer_id", "c")
                .OrderByDescending("created_at")
                .Page(2, 20);
        }
    }

    private sealed class EqualsAnyCriteria : PostgresCriteria<Row>
    {
        public EqualsAnyCriteria() : base("orders") => WhereEqualsAny("tags", "vip");
    }

    private sealed class RangeCriteria : PostgresCriteria<Row>
    {
        public RangeCriteria() : base("bookings") {
            WhereBetween("amount", 10, 100)
                .WhereRangeOverlaps("starts_at", "ends_at",
                    DateTimeOffset.Parse("2026-01-01Z"), DateTimeOffset.Parse("2026-12-31Z"));
        }
    }

    private sealed class DistinctCriteria : PostgresCriteria<Row>
    {
        public DistinctCriteria() : base("orders") => Select("region").Distinct();
    }
}
