using Aedis.Database.Abstractions;
using Aedis.Database.Postgres;
using Aedis.Exceptions;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Aedis.Database.Postgres.Tests;

/// <summary>
///     Garante a normalização das exceções do Npgsql em <see cref="DatabaseError" /> (pelo SQLSTATE e pelos
///     campos estruturados, sem depender de um banco) e o mapeamento padrão para as exceções do framework:
///     integridade vira regra de negócio com a restrição como regra; transitórios viram indisponibilidade
///     temporária; o resto segue sem tradução.
/// </summary>
public sealed class DatabaseErrorTranslationTests
{
    [Fact]
    public void Violacao_de_unicidade_e_normalizada_com_restricao_tabela_e_coluna() {
        var exception = new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505",
            tableName: "contratos", columnName: "chave", constraintName: "ux_contratos_chave");

        var error = PostgresDatabaseErrorTranslator.TryTranslate(exception);

        error.Should().NotBeNull();
        error!.Kind.Should().Be(DatabaseErrorKind.UniqueViolation);
        error.Provider.Should().Be("postgres");
        error.SqlState.Should().Be("23505");
        error.ConstraintName.Should().Be("ux_contratos_chave");
        error.TableName.Should().Be("contratos");
        error.ColumnName.Should().Be("chave");
        error.OriginalException.Should().BeSameAs(exception);
    }

    [Theory]
    [InlineData("23503", DatabaseErrorKind.ForeignKeyViolation)]
    [InlineData("23514", DatabaseErrorKind.CheckViolation)]
    [InlineData("23P01", DatabaseErrorKind.ExclusionViolation)]
    [InlineData("23502", DatabaseErrorKind.NotNullViolation)]
    [InlineData("40001", DatabaseErrorKind.SerializationFailure)]
    [InlineData("40P01", DatabaseErrorKind.Deadlock)]
    [InlineData("57014", DatabaseErrorKind.Timeout)]
    [InlineData("08006", DatabaseErrorKind.ConnectionFailure)]
    [InlineData("42601", DatabaseErrorKind.Other)]
    public void SqlState_e_classificado_na_classe_normalizada(string sqlState, DatabaseErrorKind esperado) {
        var exception = new PostgresException("erro", "ERROR", "ERROR", sqlState);

        PostgresDatabaseErrorTranslator.TryTranslate(exception)!.Kind.Should().Be(esperado);
    }

    [Fact]
    public void Excecao_nao_reconhecida_nao_e_traduzida() {
        PostgresDatabaseErrorTranslator.TryTranslate(new InvalidOperationException("x")).Should().BeNull();
    }

    [Fact]
    public void Unicidade_vira_BusinessException_com_regra_igual_a_restricao_e_sem_a_mensagem_crua() {
        var error = Error(DatabaseErrorKind.UniqueViolation, constraint: "ux_x", message: "mensagem crua do servidor");

        var mapped = DefaultDatabaseErrorMapping.Map(error);

        var business = mapped.Should().BeOfType<BusinessException>().Which;
        business.ViolationType.Should().Be(ViolationType.UniqueConstraintViolation);
        business.Rule.Should().Be("ux_x");
        business.Message.Should().NotContain("mensagem crua");
        business.InnerException.Should().BeSameAs(error.OriginalException);
    }

    [Fact]
    public void Chave_estrangeira_vira_BusinessException_ForeignKey() {
        var mapped = DefaultDatabaseErrorMapping.Map(Error(DatabaseErrorKind.ForeignKeyViolation, constraint: "fk_y"));

        mapped.Should().BeOfType<BusinessException>().Which.ViolationType.Should().Be(ViolationType.ForeignKeyViolation);
    }

    [Theory]
    [InlineData(DatabaseErrorKind.Deadlock)]
    [InlineData(DatabaseErrorKind.SerializationFailure)]
    [InlineData(DatabaseErrorKind.Timeout)]
    [InlineData(DatabaseErrorKind.ConnectionFailure)]
    public void Transitorio_vira_ServiceTemporarilyUnavailable(DatabaseErrorKind kind) {
        var mapped = DefaultDatabaseErrorMapping.Map(Error(kind));

        mapped.Should().BeOfType<ServiceTemporarilyUnavailableException>();
    }

    [Fact]
    public void Classe_Other_nao_tem_mapeamento_padrao() {
        DefaultDatabaseErrorMapping.Map(Error(DatabaseErrorKind.Other)).Should().BeNull();
    }

    private static DatabaseError Error(DatabaseErrorKind kind, string? constraint = null, string message = "erro") =>
        new(kind, "postgres", null, null, constraint, null, "tabela", message, new InvalidOperationException(message));
}
