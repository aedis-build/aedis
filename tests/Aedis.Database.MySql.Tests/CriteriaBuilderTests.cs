using Aedis.Database.MySql.Queries;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.MySql.Tests;

/// <summary>
///     Builder fluente <see cref="MySqlCriteria{TEntity}" /> (determinístico, sem banco): montagem de
///     where/and/or, grupos parentetizados, in/between, valor em coluna JSON, joins, ordenação e paginação
///     <c>LIMIT … OFFSET …</c> — com valores (e caminhos JSON) sempre parametrizados.
/// </summary>
public sealed class CriteriaBuilderTests
{
    private sealed record Row;

    private enum Status { Pending }

    [Fact]
    public void Monta_where_grupo_in_json_join_e_paginacao() {
        var (sql, parameters) = new SampleCriteria().Build();
        var p = (IDictionary<string, object>)parameters;

        sql.Should().StartWith("SELECT o.id, o.total FROM orders o");
        sql.Should().Contain("INNER JOIN customers c ON c.id = o.customer_id");
        sql.Should().Contain("status = @p0");
        sql.Should().Contain("total >= @p1");
        sql.Should().Contain("(description LIKE @p2 OR note IS NULL)");
        sql.Should().Contain("region IN (@p3, @p4)");
        sql.Should().Contain("JSON_UNQUOTE(JSON_EXTRACT(metadata, @p5)) = @p6");
        sql.Should().Contain("ORDER BY created_at DESC");
        sql.Should().EndWith("LIMIT 20 OFFSET 20");

        p["p0"].Should().Be("Pending", "enums viram string pelo nome PascalCase (EnumPersistence)");
        p["p3"].Should().Be("sa");
        p["p4"].Should().Be("na");
        p["p5"].Should().Be("$.tier", "o caminho JSON também é bind parameter");
        p["p6"].Should().Be("vip");
    }

    [Fact]
    public void WhereBetween_e_offset_sem_limit_usa_o_limite_maximo_do_MySQL() {
        var (sql, _) = new RangeCriteria().Build();

        sql.Should().Contain("amount BETWEEN @p0 AND @p1");
        sql.Should().EndWith("LIMIT 18446744073709551615 OFFSET 5");
    }

    [Fact]
    public void Distinct_select_explicito_e_sem_where_nem_paginacao() {
        var (sql, _) = new DistinctCriteria().Build();

        sql.Should().Be("SELECT DISTINCT region FROM orders");
    }

    private sealed class SampleCriteria : MySqlCriteria<Row>
    {
        public SampleCriteria() : base("orders", "o") {
            Select("o.id, o.total")
                .WhereEquals("status", Status.Pending)
                .And().WhereGreaterThanOrEqual("total", 100m)
                .Group(LogicalOperator.Or, g => g.WhereLike("description", "%a%").WhereIsNull("note"))
                .WhereIn("region", new[] { "sa", "na" })
                .WhereJsonValue("metadata", "$.tier", ComparisonOperator.Equals, "vip")
                .InnerJoin("customers", "c.id = o.customer_id", "c")
                .OrderByDescending("created_at")
                .Page(2, 20);
        }
    }

    private sealed class RangeCriteria : MySqlCriteria<Row>
    {
        public RangeCriteria() : base("bookings") => WhereBetween("amount", 10, 100).Offset(5);
    }

    private sealed class DistinctCriteria : MySqlCriteria<Row>
    {
        public DistinctCriteria() : base("orders") => Select("region").Distinct();
    }
}
