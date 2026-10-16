using Aedis.Database.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.Tests;

/// <summary>Fábricas de NamingContext, especificação de upsert com guardas e persistência de enums.</summary>
public class AbstractionsContractsTests
{
    [Fact]
    public void NamingContext_fabricas_preenchem_operacao_e_parametros() {
        NamingContext.ForTable(NamingConvention.SnakeCase, "Order").Should().BeEquivalentTo(
            new { Input = "Order", Operation = NamingOperation.TableName, AdditionalParameters = (string[]?)null });
        NamingContext.ForColumn(NamingConvention.SnakeCase, "CreatedAt").Operation.Should().Be(NamingOperation.ColumnName);
        NamingContext.ForIndex(NamingConvention.SnakeCase, "Order", "A", "B").Should().BeEquivalentTo(
            new { Input = "Order", Operation = NamingOperation.IndexName, AdditionalParameters = new[] { "A", "B" } });
        NamingContext.ForConstraint(NamingConvention.PascalCase, "fk", "Order", "CustomerId").Should().BeEquivalentTo(
            new { Input = "fk|Order", Operation = NamingOperation.ConstraintName, AdditionalParameters = new[] { "CustomerId" } });
        NamingContext.ForValidation(NamingConvention.CamelCase, "x", NamingOperation.Validation).Operation
            .Should().Be(NamingOperation.Validation);
    }

    [Fact]
    public void UpsertSpec_monta_chaves_guardas_valores_de_servidor_e_preservados() {
        var spec = UpsertSpec.OnKey("Code", "Tenant")
            .SetServerUtcNow("UpdatedAt")
            .Preserve("CreatedAt")
            .When(g => g.OrNewer("Version").OrGreater("Revision").AndExistingTrue("Active").AndNotDeleted());

        spec.KeyProperties.Should().Equal("Code", "Tenant");
        spec.UpdateMatched.Should().BeTrue();
        spec.ServerValues.Should().ContainKey("UpdatedAt").WhoseValue.Should().Be(UpsertServerValue.UtcNow);
        spec.PreservedProperties.Should().Contain("CreatedAt");
        spec.Guard!.IsEmpty.Should().BeFalse();
        spec.Guard.Freshness.Should().Equal(new FreshnessTerm("Version", FreshnessOperator.GreaterOrEqual),
            new FreshnessTerm("Revision", FreshnessOperator.Greater));
        spec.Guard.Booleans.Should().Equal(new BooleanTerm("Active", true), new BooleanTerm("IsDeleted", false));
    }

    [Fact]
    public void UpsertSpec_sem_chaves_usa_Id_e_DoNothing_limpa_tudo() {
        var spec = UpsertSpec.OnKey().SetServerUtcNow("UpdatedAt").When(g => g.Newer("V")).DoNothingOnConflict();

        spec.KeyProperties.Should().Equal("Id");
        spec.UpdateMatched.Should().BeFalse();
        spec.Guard.Should().BeNull();
        spec.ServerValues.Should().BeEmpty();
    }

    private enum Kind { Pending }

    [Fact]
    public void EnumPersistence_grava_pelo_nome_e_deixa_outros_valores_intactos() {
        EnumPersistence.ToStored(Kind.Pending).Should().Be("Pending");
        EnumPersistence.ToStored(42).Should().Be(42);
        EnumPersistence.ToStored(null).Should().BeNull();
    }
}
