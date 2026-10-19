using System.Reflection;
using Aedis.Database.Abstractions;
using Aedis.Exceptions;
using FluentAssertions;
using MySqlConnector;
using Xunit;

namespace Aedis.Database.MySql.Tests;

/// <summary>
///     Garante a normalização das exceções do MySqlConnector em <see cref="DatabaseError" /> e o mapeamento
///     padrão para as exceções do framework. <c>MySqlException</c> não tem construtor público, então a
///     classificação por <c>Number</c> usa uma instância montada por reflexão.
/// </summary>
public sealed class DatabaseErrorTranslationTests
{
    [Fact]
    public void Timeout_do_cliente_e_normalizado_como_Timeout() {
        var exception = new TimeoutException("expirou");

        var error = MySqlDatabaseErrorTranslator.TryTranslate(exception);

        error.Should().NotBeNull();
        error!.Kind.Should().Be(DatabaseErrorKind.Timeout);
        error.Provider.Should().Be("mysql");
        error.OriginalException.Should().BeSameAs(exception);
    }

    [Fact]
    public void Excecao_nao_reconhecida_nao_e_traduzida() {
        MySqlDatabaseErrorTranslator.TryTranslate(new InvalidOperationException("x")).Should().BeNull();
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

    [Theory]
    [InlineData(DatabaseErrorKind.Deadlock)]
    [InlineData(DatabaseErrorKind.Timeout)]
    [InlineData(DatabaseErrorKind.ConnectionFailure)]
    public void Transitorio_vira_ServiceTemporarilyUnavailable(DatabaseErrorKind kind) {
        var mapped = DefaultDatabaseErrorMapping.Map(Error(kind));

        mapped.Should().BeOfType<ServiceTemporarilyUnavailableException>().Which.ServiceName.Should().Be("mysql");
    }

    private static DatabaseError Error(DatabaseErrorKind kind, string? constraint = null, string message = "erro") =>
        new(kind, "mysql", null, null, constraint, null, "tabela", message, new InvalidOperationException(message));

    [Theory]
    [InlineData(1062, "Duplicate entry 'abc' for key 'contratos.ux_contratos_chave'", DatabaseErrorKind.UniqueViolation, "ux_contratos_chave", null)]
    [InlineData(1062, "Duplicate entry '1' for key 'PRIMARY'", DatabaseErrorKind.UniqueViolation, "PRIMARY", null)]
    [InlineData(1452, "Cannot add or update a child row: a foreign key constraint fails (`app`.`itens`, CONSTRAINT `fk_itens_pedido` FOREIGN KEY (`pedido_id`) REFERENCES `pedidos` (`id`))", DatabaseErrorKind.ForeignKeyViolation, "fk_itens_pedido", null)]
    [InlineData(1451, "Cannot delete or update a parent row: a foreign key constraint fails", DatabaseErrorKind.ForeignKeyViolation, null, null)]
    [InlineData(3819, "Check constraint 'chk_total_positivo' is violated.", DatabaseErrorKind.CheckViolation, "chk_total_positivo", null)]
    [InlineData(1048, "Column 'nome' cannot be null", DatabaseErrorKind.NotNullViolation, null, "nome")]
    [InlineData(1364, "Field 'nome' doesn't have a default value", DatabaseErrorKind.NotNullViolation, null, null)]
    [InlineData(1213, "Deadlock found when trying to get lock; try restarting transaction", DatabaseErrorKind.Deadlock, null, null)]
    [InlineData(1205, "Lock wait timeout exceeded; try restarting transaction", DatabaseErrorKind.Timeout, null, null)]
    [InlineData(2002, "Can't connect to local MySQL server through socket", DatabaseErrorKind.ConnectionFailure, null, null)]
    [InlineData(2003, "Can't connect to MySQL server on 'db'", DatabaseErrorKind.ConnectionFailure, null, null)]
    [InlineData(2006, "MySQL server has gone away", DatabaseErrorKind.ConnectionFailure, null, null)]
    [InlineData(2013, "Lost connection to MySQL server during query", DatabaseErrorKind.ConnectionFailure, null, null)]
    [InlineData(1040, "Too many connections", DatabaseErrorKind.ConnectionFailure, null, null)]
    [InlineData(1146, "Table 'app.x' doesn't exist", DatabaseErrorKind.Other, null, null)]
    public void MySqlException_e_classificada_pelo_Number_e_restricao_e_coluna_vem_da_mensagem(int number, string message,
        DatabaseErrorKind kind, string? constraint, string? column) {
        var exception = CreateMySqlException(number, message);

        var error = MySqlDatabaseErrorTranslator.TryTranslate(exception);

        error.Should().NotBeNull();
        error!.Kind.Should().Be(kind);
        error.Number.Should().Be(number);
        error.ConstraintName.Should().Be(constraint);
        error.ColumnName.Should().Be(column);
        error.OriginalException.Should().BeSameAs(exception);
    }

    /// <summary>MySqlException não tem construtor público: usa o construtor interno (código, SQLSTATE, mensagem).</summary>
    private static MySqlException CreateMySqlException(int number, string message) {
        const BindingFlags all = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        var ctor = typeof(MySqlException).GetConstructors(all)
            .OrderByDescending(c => c.GetParameters().Length)
            .First(c => c.GetParameters().Any(p => p.ParameterType == typeof(MySqlErrorCode)));
        var args = ctor.GetParameters().Select(object? (p) =>
            p.ParameterType == typeof(MySqlErrorCode) ? (MySqlErrorCode)number
            : p.Name == "message" ? message
            : p.ParameterType == typeof(string) ? "HY000"
            : null).ToArray();
        return (MySqlException)ctor.Invoke(args);
    }
}
