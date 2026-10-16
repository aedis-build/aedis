using System.Data;
using Aedis.Database.Abstractions;
using Aedis.Database.Queries;
using Aedis.Database.TypeHandlers;
using Aedis.Exceptions;
using Aedis.Security.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Database.Tests;

/// <summary>Componentes compartilhados pelos providers: auditoria, mapeamento padrão de erros, type handlers e critérios.</summary>
public class SharedComponentsTests
{
    private sealed class Audited
    {
        public DateTimeOffset CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public string? UpdatedReason { get; set; }
    }

    private sealed class Plain
    {
        public int Id { get; set; }
    }

    private static IAuditContext Audit(string? actor, string? reason = null) {
        var audit = Substitute.For<IAuditContext>();
        audit.CurrentActor.Returns(actor);
        audit.Now.Returns(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        audit.Reason.Returns(reason);
        return audit;
    }

    [Fact]
    public void AuditColumns_carimba_criacao_so_quando_vazia_e_atualizacao_sempre() {
        var columns = AuditColumns.For(typeof(Audited));
        var entity = new Audited { CreatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), CreatedBy = "" };

        columns.Stamp(entity, Audit("ana", "ajuste"), "system");

        columns.HasAny.Should().BeTrue();
        entity.CreatedAt.Year.Should().Be(2020, "a data de criação já estava preenchida");
        entity.CreatedBy.Should().Be("ana", "ator vazio conta como não definido");
        entity.UpdatedAt.Should().Be(new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc));
        entity.UpdatedBy.Should().Be("ana");
        entity.UpdatedReason.Should().Be("ajuste");
    }

    [Fact]
    public void AuditColumns_usa_ator_padrao_e_preserva_motivo_quando_nao_ha() {
        var entity = new Audited { UpdatedReason = "anterior" };

        AuditColumns.For(typeof(Audited)).Stamp(entity, Audit(null), "system");

        entity.CreatedAt.Should().Be(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        entity.CreatedBy.Should().Be("system");
        entity.UpdatedReason.Should().Be("anterior");
    }

    [Fact]
    public void AuditColumns_sem_colunas_e_cacheado_por_tipo() {
        AuditColumns.For(typeof(Plain)).HasAny.Should().BeFalse();
        AuditColumns.For(typeof(Plain)).Should().BeSameAs(AuditColumns.For(typeof(Plain)));
    }

    private static DatabaseError Error(DatabaseErrorKind kind, string? constraint = null) =>
        new(kind, "postgres", null, null, constraint, null, null, "erro", new InvalidOperationException("db"));

    [Theory]
    [InlineData(DatabaseErrorKind.UniqueViolation, ViolationType.UniqueConstraintViolation)]
    [InlineData(DatabaseErrorKind.ForeignKeyViolation, ViolationType.ForeignKeyViolation)]
    [InlineData(DatabaseErrorKind.ExclusionViolation, ViolationType.ConflictError)]
    [InlineData(DatabaseErrorKind.CheckViolation, ViolationType.ValidationError)]
    [InlineData(DatabaseErrorKind.NotNullViolation, ViolationType.ValidationError)]
    public void DefaultMapping_violacoes_viram_BusinessException(DatabaseErrorKind kind, ViolationType expected) {
        var ex = DefaultDatabaseErrorMapping.Map(Error(kind, "uq_orders")).Should().BeOfType<BusinessException>().Subject;

        ex.ViolationType.Should().Be(expected);
        ex.Message.Should().Contain("(uq_orders).");
        ex.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void DefaultMapping_sem_constraint_nao_acrescenta_parenteses() {
        DefaultDatabaseErrorMapping.Map(Error(DatabaseErrorKind.UniqueViolation))!.Message.Should().NotContain("(");
    }

    [Theory]
    [InlineData(DatabaseErrorKind.Deadlock, "Deadlock")]
    [InlineData(DatabaseErrorKind.SerializationFailure, "serialização")]
    [InlineData(DatabaseErrorKind.Timeout, "Tempo limite")]
    [InlineData(DatabaseErrorKind.ConnectionFailure, "conexão")]
    public void DefaultMapping_transitorios_viram_ServiceTemporarilyUnavailable(DatabaseErrorKind kind, string fragment) {
        var ex = DefaultDatabaseErrorMapping.Map(Error(kind))
            .Should().BeOfType<ServiceTemporarilyUnavailableException>().Subject;

        ex.ServiceName.Should().Be("postgres");
        ex.Message.Should().Contain(fragment);
    }

    [Fact]
    public void DefaultMapping_outros_erros_nao_sao_mapeados() {
        DefaultDatabaseErrorMapping.Map(Error(DatabaseErrorKind.Other)).Should().BeNull();
    }

    [Fact]
    public void TimeOnlyHandler_converte_e_grava_como_time() {
        var handler = new TimeOnlyTypeHandler();
        var expected = new TimeOnly(13, 45, 10);

        handler.Parse(expected.ToTimeSpan()).Should().Be(expected);
        handler.Parse(new DateTime(2026, 1, 1, 13, 45, 10)).Should().Be(expected);
        handler.Parse(expected).Should().Be(expected);
        handler.Parse("13:45:10").Should().Be(expected);
        var act = () => handler.Parse(42);
        act.Should().Throw<InvalidCastException>();

        var parameter = Substitute.For<IDbDataParameter>();
        handler.SetValue(parameter, expected);
        parameter.DbType.Should().Be(DbType.Time);
        parameter.Value.Should().Be(expected.ToTimeSpan());
    }

    [Fact]
    public void DateOnlyHandler_converte_e_grava_como_date() {
        var handler = new DateOnlyTypeHandler();
        var expected = new DateOnly(2026, 10, 16);

        handler.Parse(new DateTime(2026, 10, 16, 8, 0, 0)).Should().Be(expected);
        handler.Parse(expected).Should().Be(expected);
        handler.Parse("2026-10-16").Should().Be(expected);
        var act = () => handler.Parse(42L);
        act.Should().Throw<InvalidCastException>();

        var parameter = Substitute.For<IDbDataParameter>();
        handler.SetValue(parameter, expected);
        parameter.DbType.Should().Be(DbType.Date);
        parameter.Value.Should().Be(new DateTime(2026, 10, 16));
    }

    [Fact]
    public void RawCriteria_devolve_sql_e_parametros() {
        var parameters = new { Id = 1 };

        new RawCriteria<Plain>("select 1 where id = @Id", parameters).Build().Should().Be(("select 1 where id = @Id", (object)parameters));
        new RawCriteria<Plain>("select 1").Build().Parameters.Should().NotBeNull();
        new RawCriteria<Plain>("x") { IsDistinct = true }.IsDistinct.Should().BeTrue();
        var act = () => new RawCriteria<Plain>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("orders", true)]
    [InlineData("dbo.Orders_2", true)]
    [InlineData("_x", true)]
    [InlineData("1orders", false)]
    [InlineData("orders; drop table x", false)]
    [InlineData("a.b.c", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SqlIdentifier_aceita_so_identificadores_simples(string? identifier, bool valid) {
        SqlIdentifier.IsValid(identifier).Should().Be(valid);

        var act = () => SqlIdentifier.Validate(identifier!);
        if (valid) act().Should().Be(identifier);
        else act.Should().Throw<ArgumentException>().WithParameterName("identifier");
    }

    [Fact]
    public void DatabaseOptions_tem_defaults_seguros() {
        var options = new DatabaseOptions();

        options.NamingConvention.Should().Be(NamingConvention.SnakeCase);
        options.DefaultAuditActor.Should().Be("system");
        options.BulkInsertChunkSize.Should().Be(2_000);
        DatabaseOptions.SectionName.Should().Be("Database");
    }
}
