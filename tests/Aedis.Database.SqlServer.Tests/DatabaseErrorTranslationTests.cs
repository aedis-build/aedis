using Aedis.Database.Abstractions;
using Aedis.Database.SqlServer;
using Aedis.Exceptions;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>
///     Garante a normalização das exceções do SqlClient em <see cref="DatabaseError" /> e o mapeamento padrão
///     para as exceções do framework. <c>SqlException</c> não tem construtor público, então a classificação
///     por <c>Number</c> é coberta pelos testes de integração; aqui ficam os caminhos construíveis sem banco
///     (timeout, exceções não reconhecidas) e a tabela de mapeamento.
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
}
