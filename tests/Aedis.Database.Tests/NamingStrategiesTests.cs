using Aedis.Database.Abstractions;
using Aedis.Database.Naming;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.Tests;

/// <summary>Conversão e validação de nomes pelas três convenções e resolução pelo resolvedor.</summary>
public class NamingStrategiesTests
{
    private static NamingContext Ctx(string input, NamingOperation op, NamingConvention conv,
        params string[] extra) =>
        new() { Input = input, Operation = op, Convention = conv, AdditionalParameters = extra.Length == 0 ? null : extra };

    [Theory]
    [InlineData("OrderItem", "order_items")]
    [InlineData("Address", "addresses")]
    [InlineData("Box", "boxes")]
    [InlineData("Quiz", "quizes")]
    [InlineData("Batch", "batches")]
    [InlineData("Dish", "dishes")]
    [InlineData("Category", "categories")]
    [InlineData("Day", "days")]
    [InlineData("", "")]
    public void SnakeCase_tabela_pluraliza(string input, string expected) {
        new SnakeCaseNamingStrategy().Convert(Ctx(input, NamingOperation.TableName, NamingConvention.SnakeCase))
            .Should().Be(expected);
    }

    [Fact]
    public void SnakeCase_coluna_indice_e_constraint() {
        var s = new SnakeCaseNamingStrategy();

        s.Convert(Ctx("CreatedAt", NamingOperation.ColumnName, NamingConvention.SnakeCase)).Should().Be("created_at");
        s.Convert(Ctx("OrderItem", NamingOperation.IndexName, NamingConvention.SnakeCase, "OrderId", "Sku"))
            .Should().Be("idx_order_item_order_id_sku");
        s.Convert(Ctx("fk|OrderItem", NamingOperation.ConstraintName, NamingConvention.SnakeCase, "OrderId"))
            .Should().Be("fk_order_item_order_id");
        s.Convert(Ctx("pk|OrderItem", NamingOperation.ConstraintName, NamingConvention.SnakeCase))
            .Should().Be("pk_order_item");
        s.CanHandle(Ctx("x", NamingOperation.ColumnName, NamingConvention.SnakeCase)).Should().BeTrue();
        s.CanHandle(Ctx("x", NamingOperation.ColumnName, NamingConvention.PascalCase)).Should().BeFalse();
    }

    [Theory]
    [InlineData("order_items", true)]
    [InlineData("a1_b2", true)]
    [InlineData("OrderItems", false)]
    [InlineData("order__items", false)]
    [InlineData("_orders", false)]
    public void SnakeCase_valida_o_formato(string input, bool valid) {
        var ok = new SnakeCaseNamingStrategy()
            .Validate(Ctx(input, NamingOperation.TableName, NamingConvention.SnakeCase), out var error);

        ok.Should().Be(valid);
        (error is null).Should().Be(valid);
        if (!valid) error.Should().Contain("Tabela").And.Contain(input);
    }

    [Fact]
    public void SnakeCase_mensagem_de_coluna() {
        new SnakeCaseNamingStrategy()
            .Validate(Ctx("Bad", NamingOperation.ColumnName, NamingConvention.SnakeCase), out var error);

        error.Should().Contain("Coluna");
    }

    [Theory]
    [InlineData("order_item", NamingOperation.TableName, "OrderItems")]
    [InlineData("orders", NamingOperation.TableName, "Orders")]
    [InlineData("created_at", NamingOperation.ColumnName, "CreatedAt")]
    [InlineData("", NamingOperation.ColumnName, "")]
    public void PascalCase_converte(string input, NamingOperation op, string expected) {
        var s = new PascalCaseNamingStrategy();

        s.Convert(Ctx(input, op, NamingConvention.PascalCase)).Should().Be(expected);
        s.Validate(Ctx(input, op, NamingConvention.PascalCase), out var error).Should().BeTrue();
        error.Should().BeNull();
        s.CanHandle(Ctx(input, op, NamingConvention.PascalCase)).Should().BeTrue();
    }

    [Theory]
    [InlineData("order_item", NamingOperation.TableName, "orderItems")]
    [InlineData("Orders", NamingOperation.TableName, "orders")]
    [InlineData("CreatedAt", NamingOperation.ColumnName, "createdAt")]
    [InlineData("", NamingOperation.ColumnName, "")]
    public void CamelCase_converte(string input, NamingOperation op, string expected) {
        var s = new CamelCaseNamingStrategy();

        s.Convert(Ctx(input, op, NamingConvention.CamelCase)).Should().Be(expected);
        s.Validate(Ctx(input, op, NamingConvention.CamelCase), out _).Should().BeTrue();
        s.CanHandle(Ctx(input, op, NamingConvention.CamelCase)).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_e_noop_nas_tres() {
        var ctx = Ctx("x", NamingOperation.ColumnName, NamingConvention.SnakeCase);

        await new SnakeCaseNamingStrategy().ExecuteAsync(ctx);
        await new PascalCaseNamingStrategy().ExecuteAsync(ctx);
        await new CamelCaseNamingStrategy().ExecuteAsync(ctx);
    }

    [Fact]
    public void Resolver_escolhe_pela_convencao_e_falha_sem_estrategia() {
        var resolver = new NamingStrategyResolver([new SnakeCaseNamingStrategy(), new PascalCaseNamingStrategy()]);

        resolver.GetStrategy(Ctx("x", NamingOperation.ColumnName, NamingConvention.PascalCase))
            .Should().BeOfType<PascalCaseNamingStrategy>();
        var act = () => resolver.GetStrategy(Ctx("x", NamingOperation.ColumnName, NamingConvention.CamelCase));
        act.Should().Throw<InvalidOperationException>().WithMessage("*CamelCase*");
    }
}
