using Aedis.Database.Abstractions;
using Aedis.Database.SqlServer;
using Aedis.Exceptions;
using System.Reflection;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>
///     Garante a normalização das exceções do SqlClient em <see cref="DatabaseError" /> e o mapeamento padrão
///     para as exceções do framework. <c>SqlException</c> não tem construtor público, então a classificação
///     por <c>Number</c> usa uma instância montada por reflexão (como faz o próprio SqlClient nos testes dele).
/// </summary>
public sealed class DatabaseErrorTranslationTests
{
    [Fact]
    public void Timeout_do_cliente_e_normalizado_como_Timeout() {
        var exception = new TimeoutException("expirou");

        var error = SqlServerDatabaseErrorTranslator.TryTranslate(exception);

        error.Should().NotBeNull();
        error!.Kind.Should().Be(DatabaseErrorKind.Timeout);
        error.Provider.Should().Be("sqlserver");
        error.OriginalException.Should().BeSameAs(exception);
    }

    [Fact]
    public void Excecao_nao_reconhecida_nao_e_traduzida() {
        SqlServerDatabaseErrorTranslator.TryTranslate(new InvalidOperationException("x")).Should().BeNull();
    }

    [Fact]
    public void Unicidade_vira_BusinessException_com_regra_igual_a_restricao_e_sem_a_mensagem_crua() {
        var error = Error(DatabaseErrorKind.UniqueViolation, constraint: "UX_X", message: "mensagem crua do servidor");

        var mapped = DefaultDatabaseErrorMapping.Map(error);

        var business = mapped.Should().BeOfType<BusinessException>().Which;
        business.ViolationType.Should().Be(ViolationType.UniqueConstraintViolation);
        business.Rule.Should().Be("UX_X");
        business.Message.Should().NotContain("mensagem crua");
        business.InnerException.Should().BeSameAs(error.OriginalException);
    }

    [Fact]
    public void Chave_estrangeira_vira_BusinessException_ForeignKey() {
        var mapped = DefaultDatabaseErrorMapping.Map(Error(DatabaseErrorKind.ForeignKeyViolation, constraint: "FK_Y"));

        mapped.Should().BeOfType<BusinessException>().Which.ViolationType.Should().Be(ViolationType.ForeignKeyViolation);
    }

    [Theory]
    [InlineData(DatabaseErrorKind.Deadlock)]
    [InlineData(DatabaseErrorKind.SerializationFailure)]
    [InlineData(DatabaseErrorKind.Timeout)]
    [InlineData(DatabaseErrorKind.ConnectionFailure)]
    public void Transitorio_vira_ServiceTemporarilyUnavailable(DatabaseErrorKind kind) {
        var mapped = DefaultDatabaseErrorMapping.Map(Error(kind));

        var unavailable = mapped.Should().BeOfType<ServiceTemporarilyUnavailableException>().Which;
        unavailable.ServiceName.Should().Be("sqlserver");
    }

    [Fact]
    public void Classe_Other_nao_tem_mapeamento_padrao() {
        DefaultDatabaseErrorMapping.Map(Error(DatabaseErrorKind.Other)).Should().BeNull();
    }

    private static DatabaseError Error(DatabaseErrorKind kind, string? constraint = null, string message = "erro") =>
        new(kind, "sqlserver", null, null, constraint, null, "Tabela", message, new InvalidOperationException(message));

    [Theory]
    [InlineData(2627, "Violation of UNIQUE KEY constraint 'UX_X'. Cannot insert duplicate key.", DatabaseErrorKind.UniqueViolation, "UX_X")]
    [InlineData(2601, "Cannot insert duplicate key row in object 'dbo.t' with unique index 'IX_T'.", DatabaseErrorKind.UniqueViolation, null)]
    [InlineData(547, "The INSERT statement conflicted with the FOREIGN KEY constraint \"FK_Y\".", DatabaseErrorKind.ForeignKeyViolation, "FK_Y")]
    [InlineData(547, "The INSERT statement conflicted with the CHECK constraint \"CK_Z\".", DatabaseErrorKind.CheckViolation, "CK_Z")]
    [InlineData(515, "Cannot insert the value NULL into column 'c'.", DatabaseErrorKind.NotNullViolation, null)]
    [InlineData(1205, "Transaction was deadlocked and has been chosen as the deadlock victim.", DatabaseErrorKind.Deadlock, null)]
    [InlineData(3960, "Snapshot isolation transaction aborted due to update conflict.", DatabaseErrorKind.SerializationFailure, null)]
    [InlineData(-2, "Execution Timeout Expired.", DatabaseErrorKind.Timeout, null)]
    [InlineData(18456, "Login failed for user 'app'.", DatabaseErrorKind.ConnectionFailure, null)]
    [InlineData(208, "Invalid object name 'x'.", DatabaseErrorKind.Other, null)]
    public void SqlException_e_classificada_pelo_Number_e_a_restricao_vem_da_mensagem(int number, string message,
        DatabaseErrorKind kind, string? constraint) {
        var exception = CreateSqlException(number, message);

        var error = SqlServerDatabaseErrorTranslator.TryTranslate(exception);

        error.Should().NotBeNull();
        error!.Kind.Should().Be(kind);
        error.Number.Should().Be(number);
        error.ConstraintName.Should().Be(constraint);
        error.OriginalException.Should().BeSameAs(exception);
    }

    /// <summary>SqlException não tem construtor público: monta erro, coleção e exceção pelos membros internos do SqlClient.</summary>
    private static SqlException CreateSqlException(int number, string message) {
        const BindingFlags all = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        var errorCtor = typeof(SqlError).GetConstructors(all).OrderByDescending(c => c.GetParameters().Length).First();
        var args = errorCtor.GetParameters().Select(p => p.Name switch {
            "infoNumber" => number,
            "errorMessage" => message,
            _ => p.ParameterType == typeof(string) ? "" : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null
        }).ToArray();
        var collection = Activator.CreateInstance(typeof(SqlErrorCollection), true)!;
        typeof(SqlErrorCollection).GetMethod("Add", all)!.Invoke(collection, [errorCtor.Invoke(args)]);
        var create = typeof(SqlException).GetMethod("CreateException", all, [typeof(SqlErrorCollection), typeof(string)])!;
        return (SqlException)create.Invoke(null, [collection, "16.0"])!;
    }
}
