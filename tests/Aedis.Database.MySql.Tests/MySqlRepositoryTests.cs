using Aedis.Database.Abstractions;
using Aedis.Database.MySql.Queries;
using Aedis.Database.Naming;
using Aedis.Database.Queries;
using Aedis.Domain.Entities;
using Aedis.Exceptions;
using Aedis.Security.Abstractions;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using NSubstitute;
using Testcontainers.MySql;
using Xunit;

namespace Aedis.Database.MySql.Tests;

/// <summary>
///     Repositório convention-based contra um MySQL real: o template <c>GetUpsertSpec()</c> dirigindo upsert
///     tanto no Save quanto no bulk (com e sem guarda), busca via <see cref="ICriteria{TEntity}" />,
///     <see cref="RawCriteria{TEntity}" /> seguro contra injeção, a guarda <see cref="SqlIdentifier" />,
///     auditoria/soft-delete por herança, tradução de erros e sessões.
/// </summary>
public sealed class MySqlRepositoryTests : IClassFixture<MySqlRepositoryTests.RepoFixture>
{
    private readonly RepoFixture _fixture;

    public MySqlRepositoryTests(RepoFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetUpsertSpec_dirige_upsert_no_save_e_no_bulk() {
        var table = await _fixture.CreateTableAsync();
        var repo = _fixture.Upsert(table);
        var id = Guid.NewGuid();

        await repo.SaveAsync(NewOrder(id, "v1", OrderStatus.Pending));
        (await repo.GetByIdAsync(id))!.Description.Should().Be("v1");

        await repo.SaveAsync(NewOrder(id, "v2", OrderStatus.Settled));
        var updated = await repo.GetByIdAsync(id);
        updated!.Description.Should().Be("v2");
        updated.Status.Should().Be(OrderStatus.Settled);

        var ids = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToList();
        await repo.BulkInsertAsync(ids.Select(i => NewOrder(i, "bulk-1", OrderStatus.Pending)));
        await repo.BulkInsertAsync(ids.Select(i => NewOrder(i, "bulk-2", OrderStatus.Pending)));

        (await _fixture.CountAsync(table)).Should().Be(501, "upsert no bulk não duplica");
        (await _fixture.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE description = 'bulk-2'"))
            .Should().Be(500);
    }

    [Fact]
    public async Task BulkInsert_simples_e_busca_por_criteria() {
        var table = await _fixture.CreateTableAsync();
        var repo = _fixture.Plain(table);
        var orders = Enumerable.Range(0, 1_000)
            .Select(i => NewOrder(Guid.NewGuid(), $"o-{i}", OrderStatus.Pending, i)).ToList();

        await repo.BulkInsertAsync(orders);

        (await repo.FindAsync(new RawCriteria<Order>($"SELECT * FROM {table}"))).Should().HaveCount(1_000);
        (await repo.CountAsync(new RawCriteria<Order>($"SELECT * FROM {table} WHERE total >= @min",
            new { min = 500m }))).Should().Be(500);
        (await repo.FindAsync(new SettledOrPricey(table, 990))).Should().HaveCount(9);
    }

    [Fact]
    public async Task RawCriteria_e_seguro_contra_injecao_em_valores() {
        var table = await _fixture.CreateTableAsync();
        var repo = _fixture.Plain(table);
        await repo.SaveAsync(NewOrder(Guid.NewGuid(), "real", OrderStatus.Pending));

        var attack = $"x'; DROP TABLE {table}; -- ";
        var result = await repo.FindAsync(
            new RawCriteria<Order>($"SELECT * FROM {table} WHERE description = @desc", new { desc = attack }));

        result.Should().BeEmpty("nenhuma linha tem essa descrição literal");
        (await _fixture.CountAsync(table)).Should().Be(1, "a tabela continua intacta — nada foi dropado");
    }

    [Fact]
    public async Task Exists_Query_BulkUpdate_Command_e_delete_fisico_sem_soft_delete() {
        var table = await _fixture.CreateTableAsync();
        var repo = _fixture.Plain(table);
        var id = Guid.NewGuid();
        await repo.SaveAsync(NewOrder(id, "a", OrderStatus.Pending, 10));
        await repo.BulkInsertChunkedAsync([NewOrder(Guid.NewGuid(), "b", OrderStatus.Pending, 20)]);

        (await repo.ExistsAsync(id)).Should().BeTrue();
        (await repo.QueryAsync(new RawCriteria<Projection>(
                $"SELECT description AS Description, total AS Total FROM {table} WHERE id = @id", new { id })))
            .Should().ContainSingle(p => p.Description == "a" && p.Total == 10);

        await repo.BulkUpdateAsync([NewOrder(id, "a2", OrderStatus.Settled, 11)]);
        (await repo.GetByIdAsync(id))!.Description.Should().Be("a2");
        (await repo.CommandAsync(new RawCriteria<Order>($"UPDATE {table} SET total = 0"))).Should().Be(2);

        await repo.DeleteAsync(id);
        (await repo.ExistsAsync(id)).Should().BeFalse("sem soft delete a linha some de fato");
        (await _fixture.CountAsync(table)).Should().Be(1L);
    }

    [Fact]
    public async Task Erro_do_banco_na_escrita_vira_BusinessException_com_a_restricao_e_a_sessao_e_revertida() {
        var table = await _fixture.CreateTableAsync();
        var repo = _fixture.Plain(table);
        var id = Guid.NewGuid();
        await repo.SaveAsync(NewOrder(id, "a", OrderStatus.Pending));

        var business = (await FluentActions.Awaiting(() => repo.SaveAsync(NewOrder(id, "dup", OrderStatus.Pending)))
            .Should().ThrowAsync<BusinessException>()).Which;

        business.ViolationType.Should().Be(ViolationType.UniqueConstraintViolation);
        business.Rule.Should().Be("PRIMARY", "o nome da chave vem do formato fixo da mensagem do servidor");
        (await _fixture.CountAsync(table)).Should().Be(1L);
    }

    [Fact]
    public async Task Erro_na_leitura_passa_pelo_hook_OnDatabaseError_e_sem_tradutor_e_relancado_cru() {
        var table = await _fixture.CreateTableAsync();
        var missing = new RawCriteria<Order>("SELECT * FROM tabela_inexistente");

        await FluentActions.Awaiting(() => _fixture.Plain(table).FindAsync(missing))
            .Should().ThrowAsync<MySqlException>("erro Other sem mapeamento padrão segue cru");
        await FluentActions.Awaiting(() => _fixture.Hooked(table).FindAsync(missing))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("mysql:Other");
    }

    [Fact]
    public async Task UnitOfWork_guarda_escrita_em_leitura_transacao_dupla_e_reverte_no_descarte() {
        await using (var readOnly = new UnitOfWork(new MySqlConnection(_fixture.ConnectionString), true, NullLogger<UnitOfWork>.Instance))
            await FluentActions.Awaiting(() => readOnly.ExecuteAsync("SELECT 1")).Should().ThrowAsync<InvalidOperationException>();

        var table = await _fixture.CreateTableAsync();
        var uow = new UnitOfWork(new MySqlConnection(_fixture.ConnectionString), false, NullLogger<UnitOfWork>.Instance);
        await uow.BeginTransactionAsync();
        await FluentActions.Awaiting(() => uow.BeginTransactionAsync()).Should().ThrowAsync<InvalidOperationException>();
        uow.HasActiveTransaction().Should().BeTrue();
        await uow.ExecuteAsync($"INSERT INTO {table} (id) VALUES (@id)", new { id = Guid.NewGuid() });
        await uow.RollbackAsync();
        uow.HasActiveTransaction().Should().BeFalse();
        (await _fixture.CountAsync(table)).Should().Be(0L);

        await uow.BeginTransactionAsync();
        await uow.ExecuteAsync($"INSERT INTO {table} (id) VALUES (@id)", new { id = Guid.NewGuid() });
        await uow.DisposeAsync();
        await uow.DisposeAsync();
        (await _fixture.CountAsync(table)).Should().Be(0L, "descarte sem commit reverte");

        var connection = new MySqlConnection(_fixture.ConnectionString);
        var broken = new UnitOfWork(connection, false, NullLogger<UnitOfWork>.Instance);
        await broken.BeginTransactionAsync();
        await connection.CloseAsync();
        await FluentActions.Awaiting(() => broken.DisposeAsync().AsTask()).Should().NotThrowAsync("falha no rollback do descarte é só logada");
    }

    [Fact]
    public async Task Replicas_de_leitura_sao_usadas_em_rodizio_e_sem_transacao() {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Database:ConnectionString"] = _fixture.ConnectionString,
            ["Database:ReadConnectionStrings:0"] = _fixture.ConnectionString,
            ["Database:ReadConnectionStrings:1"] = _fixture.ConnectionString
        }).Build();
        await using var provider = new ServiceCollection().AddLogging().AddAedisMySql(config).BuildServiceProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();

        for (var i = 0; i < 3; i++) {
            await using var session = await factory.CreateReadSessionAsync();
            session.IsReadOnly.Should().BeTrue();
            session.HasActiveTransaction().Should().BeFalse("leitura roda em autocommit");
            (await session.QuerySingleOrDefaultAsync<int>("SELECT 1")).Should().Be(1);
            await session.CommitAsync();
        }
    }

    public sealed class Projection
    {
        public string Description { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }

    /// <summary>Repositório de teste que traduz qualquer erro do banco pelo hook, inclusive os sem mapeamento padrão.</summary>
    private sealed class HookedOrderRepository(IUnitOfWorkFactory factory, NamingStrategyResolver naming,
        IOptions<DatabaseOptions> options, MySqlBulkInserter inserter, string table)
        : MySqlRepository<Order, Guid>(factory, NullLogger<MySqlRepository<Order, Guid>>.Instance, naming,
            options, inserter, table)
    {
        protected override Exception? OnDatabaseError(DatabaseError error) => new InvalidOperationException($"{error.Provider}:{error.Kind}");
    }

    [Theory]
    [InlineData("orders", true)]
    [InlineData("app.orders", true)]
    [InlineData("orders; DROP TABLE x", false)]
    [InlineData("orders--", false)]
    [InlineData("orders`", false)]
    public void SqlIdentifier_aceita_so_identificadores_seguros(string identifier, bool valid) {
        SqlIdentifier.IsValid(identifier).Should().Be(valid);
        if (!valid)
            FluentActions.Invoking(() => SqlIdentifier.Validate(identifier)).Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Upsert condicional pela <see cref="UpsertSpec" /> portável (a mesma dos providers PostgreSQL e SQL
    ///     Server): só atualiza se o dado de entrada for mais novo (timestamp OU sequencial), preserva
    ///     <c>created_at</c>, carimba <c>updated_at</c> no servidor e ignora linhas soft-deleted. O mesmo
    ///     template dirige Save e BulkInsertChunked — no MySQL, via <c>UPDATE … WHERE guarda</c> + INSERT das
    ///     linhas ausentes.
    /// </summary>
    [Fact]
    public async Task Upsert_condicional_so_atualiza_se_mais_novo_preserva_created_e_ignora_deletado() {
        var table = $"metrics_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($@"CREATE TABLE {table} (
            id CHAR(36) PRIMARY KEY, value DECIMAL(18,2), source_seq BIGINT,
            observed_at DATETIME(6), created_at DATETIME(6), updated_at DATETIME(6),
            is_deleted TINYINT(1) NOT NULL DEFAULT 0)");
        var repo = new SpecMetricRepository(_fixture.Factory, _fixture.Naming, _fixture.OptionsAccessor,
            _fixture.Inserter, table);

        var id = Guid.NewGuid();
        var created = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var t0 = DateTimeOffset.Parse("2026-03-01T00:00:00Z");

        await repo.SaveAsync(new Metric { Id = id, Value = 10, SourceSeq = 1, ObservedAt = t0, CreatedAt = created, UpdatedAt = created });
        Task<decimal> Value() => _fixture.ScalarAsync<decimal>($"SELECT value FROM {table} WHERE id = '{id}'");
        Task<DateTime> UpdatedAt() => _fixture.ScalarAsync<DateTime>($"SELECT updated_at FROM {table} WHERE id = '{id}'");
        (await UpdatedAt()).Should().Be(created.UtcDateTime, "na inserção vale o valor que entra");

        await repo.SaveAsync(new Metric { Id = id, Value = 15, SourceSeq = 1, ObservedAt = t0.AddDays(1), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = created });
        (await Value()).Should().Be(15, "dado mais novo pelo Save atualiza");
        (await UpdatedAt()).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5), "updated_at vem do relógio do servidor, não do valor que entra");

        await repo.BulkInsertChunkedAsync([
            new Metric { Id = id, Value = 20, SourceSeq = 2, ObservedAt = t0.AddDays(10), CreatedAt = DateTimeOffset.UtcNow },
            new Metric { Id = Guid.NewGuid(), Value = 1, SourceSeq = 1, ObservedAt = t0, CreatedAt = created }
        ]);
        (await Value()).Should().Be(20, "dado mais novo pelo bulk atualiza");
        (await _fixture.CountAsync(table)).Should().Be(2, "a linha ausente é inserida pelo mesmo bulk");
        (await _fixture.ScalarAsync<DateTime>($"SELECT created_at FROM {table} WHERE id = '{id}'"))
            .Should().Be(created.UtcDateTime, "created_at é preservado pela spec");

        await repo.BulkInsertChunkedAsync([
            new Metric { Id = id, Value = 99, SourceSeq = 1, ObservedAt = t0.AddDays(-10), CreatedAt = created }
        ]);
        (await Value()).Should().Be(20, "dado mais velho é ignorado");

        await repo.SaveAsync(new Metric { Id = id, Value = 98, SourceSeq = 1, ObservedAt = t0.AddDays(-10), CreatedAt = created });
        (await Value()).Should().Be(20, "dado mais velho é ignorado também no Save");

        await _fixture.ExecAsync($"UPDATE {table} SET is_deleted = 1 WHERE id = '{id}'");
        await repo.BulkInsertChunkedAsync([
            new Metric { Id = id, Value = 77, SourceSeq = 9, ObservedAt = t0.AddDays(99), CreatedAt = created }
        ]);
        (await Value()).Should().Be(20, "linha soft-deleted não é atualizada");
    }

    [Fact]
    public async Task Json_em_save_bulk_e_criteria_com_JSON_EXTRACT() {
        var table = $"tagged_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($"CREATE TABLE {table} (id CHAR(36) PRIMARY KEY, metadata JSON NULL)");
        var repo = _fixture.Repo<Tagged>(table);

        var vip = new Tagged { Id = Guid.NewGuid(), Metadata = new Dictionary<string, string?> { ["tier"] = "vip", ["note"] = null } };
        await repo.SaveAsync(vip);
        await repo.BulkInsertAsync([
            new Tagged { Id = Guid.NewGuid(), Metadata = new Dictionary<string, string?> { ["tier"] = "gold" } },
            new Tagged { Id = Guid.NewGuid(), Metadata = null }
        ]);

        (await repo.FindAsync(new ByTier(table, "vip"))).Should().ContainSingle().Which.Id.Should().Be(vip.Id);
        (await repo.FindAsync(new ByTier(table, "gold"))).Should().HaveCount(1);
        var loaded = await repo.GetByIdAsync(vip.Id);
        loaded!.Metadata.Should().Contain("tier", "vip");
        loaded.Metadata.Should().ContainKey("note").WhoseValue.Should().BeNull();
    }

    [Fact]
    public async Task Auditoria_carimba_created_updated_by_at_e_reason_quando_presentes() {
        var table = $"audited_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($@"CREATE TABLE {table} (
            id CHAR(36) PRIMARY KEY, name VARCHAR(100),
            created_at DATETIME(6), created_by VARCHAR(100),
            updated_at DATETIME(6), updated_by VARCHAR(100), updated_reason VARCHAR(200))");

        var now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var audit = new FakeAudit("alice", now, "correcao manual");
        var repo = _fixture.Repo<Audited>(table, audit);

        var entity = new Audited { Id = Guid.NewGuid(), Name = "x" };
        await repo.SaveAsync(entity);

        entity.CreatedBy.Should().Be("alice", "o objeto também é carimbado");
        (await _fixture.ScalarAsync<string>($"SELECT created_by FROM {table} WHERE id='{entity.Id}'"))
            .Should().Be("alice");
        (await _fixture.ScalarAsync<DateTime>($"SELECT created_at FROM {table} WHERE id='{entity.Id}'"))
            .Should().Be(now.UtcDateTime);
        (await _fixture.ScalarAsync<string>($"SELECT updated_by FROM {table} WHERE id='{entity.Id}'"))
            .Should().Be("alice");
        (await _fixture.ScalarAsync<string>($"SELECT updated_reason FROM {table} WHERE id='{entity.Id}'"))
            .Should().Be("correcao manual");

        await repo.BulkInsertAsync([new Audited { Id = Guid.NewGuid(), Name = "b" }]);
        (await _fixture.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE created_by = 'alice'"))
            .Should().Be(2);
    }

    [Fact]
    public async Task Sem_usuario_logado_grava_o_ator_default_visivel() {
        var table = $"audited_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($@"CREATE TABLE {table} (
            id CHAR(36) PRIMARY KEY, name VARCHAR(100),
            created_at DATETIME(6), created_by VARCHAR(100),
            updated_at DATETIME(6), updated_by VARCHAR(100), updated_reason VARCHAR(200))");

        var repo = _fixture.Repo<Audited>(table, new FakeAudit(null, DateTimeOffset.UtcNow, null));
        var entity = new Audited { Id = Guid.NewGuid(), Name = "x" };
        await repo.SaveAsync(entity);

        entity.CreatedBy.Should().Be("system", "default visível quando não há usuário logado");
        (await _fixture.ScalarAsync<string>($"SELECT updated_by FROM {table} WHERE id='{entity.Id}'"))
            .Should().Be("system");
    }

    public sealed class Audited
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public string? UpdatedReason { get; set; }
    }

    [Fact]
    public async Task AuditableAggregateRoot_da_auditoria_e_soft_delete_por_heranca_sem_boilerplate() {
        var table = $"calendarios_{Guid.NewGuid():N}";
        await _fixture.ExecAsync(CalendarioDdl(table));

        var now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var repo = _fixture.Repo<CalendarioEscopo>(table, new FakeAudit("bob", now, "ajuste de escopo"));

        var calendario = new CalendarioEscopo { Id = Guid.NewGuid(), Codigo = "C1", Nome = "Escopo" };
        await repo.SaveAsync(calendario);

        calendario.CreatedBy.Should().Be("bob", "a entidade só herda — o repo carimba");
        (await _fixture.ScalarAsync<string>($"SELECT created_by FROM {table} WHERE id='{calendario.Id}'"))
            .Should().Be("bob");
        (await _fixture.ScalarAsync<string>($"SELECT updated_reason FROM {table} WHERE id='{calendario.Id}'"))
            .Should().Be("ajuste de escopo");

        await repo.DeleteAsync(calendario.Id);
        (await _fixture.ScalarAsync<bool>($"SELECT is_deleted FROM {table} WHERE id='{calendario.Id}'"))
            .Should().BeTrue();
        (await _fixture.ScalarAsync<string>($"SELECT deleted_by FROM {table} WHERE id='{calendario.Id}'"))
            .Should().Be("bob");

        (await repo.GetByIdAsync(calendario.Id)).Should().BeNull();
        (await repo.ExistsAsync(calendario.Id)).Should().BeFalse();
    }

    public sealed class CalendarioEscopo : AuditableAggregateRoot<Guid>
    {
        public string Codigo { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
    }

    /// <summary>
    ///     Contrato: herdar de AuditableAggregateRoot é o opt-in; criar as colunas na migration é
    ///     responsabilidade do app. Sem schema generator — se faltar uma coluna (aqui, a tabela é criada
    ///     sem <c>updated_reason</c>), o Save falha na hora com erro explícito do MySQL (1054 unknown column).
    /// </summary>
    [Fact]
    public async Task Coluna_de_auditoria_ausente_no_schema_falha_claro_no_save() {
        var table = $"incompleto_{Guid.NewGuid():N}";
        await _fixture.ExecAsync(CalendarioDdl(table).Replace(", updated_reason VARCHAR(200)", string.Empty));
        var repo = _fixture.Repo<CalendarioEscopo>(table, new FakeAudit("bob", DateTimeOffset.UtcNow, "motivo"));

        var act = async () =>
            await repo.SaveAsync(new CalendarioEscopo { Id = Guid.NewGuid(), Codigo = "C", Nome = "N" });

        var exception = (await act.Should().ThrowAsync<MySqlException>()).Which;
        exception.Number.Should().Be(1054, "unknown column — fail-fast, sem DDL automático");
        exception.Message.Should().Contain("updated_reason");
    }

    [Fact]
    public async Task Cadeia_DI_completa_carimba_o_usuario_logado_automaticamente() {
        var table = "calendario_escopos";
        await _fixture.ExecAsync(CalendarioDdl(table).Replace("CREATE TABLE", "CREATE TABLE IF NOT EXISTS"));

        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.Id.Returns("joana");

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Database:ConnectionString"] = _fixture.ConnectionString
        }).Build();

        var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(user)
            .AddAedisAuditContext()
            .AddAedisMySql(config)
            .BuildServiceProvider();

        var id = Guid.NewGuid();
        using (var scope = provider.CreateScope()) {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<CalendarioEscopo, Guid>>();
            await repo.SaveAsync(new CalendarioEscopo { Id = id, Codigo = "C1", Nome = "Escopo" });
        }

        (await _fixture.ScalarAsync<string>($"SELECT created_by FROM {table} WHERE id='{id}'"))
            .Should().Be("joana", "o usuário logado é carimbado automaticamente pela cadeia de DI");

        await _fixture.ExecAsync($"DROP TABLE {table}");
    }

    private static string CalendarioDdl(string table) => $@"CREATE TABLE {table} (
            id CHAR(36) PRIMARY KEY, codigo VARCHAR(50), nome VARCHAR(100),
            created_at DATETIME(6), created_by VARCHAR(100), updated_at DATETIME(6), updated_by VARCHAR(100), updated_reason VARCHAR(200),
            is_deleted TINYINT(1) NOT NULL DEFAULT 0, deleted_at DATETIME(6), deleted_by VARCHAR(100))";

    private sealed record FakeAudit(string? CurrentActor, DateTimeOffset Now, string? Reason) : IAuditContext;

    public sealed class Tagged
    {
        public Guid Id { get; set; }
        public Dictionary<string, string?>? Metadata { get; set; }
    }

    private sealed class ByTier : MySqlCriteria<Tagged>
    {
        public ByTier(string table, string tier) : base(table) =>
            WhereJsonValue("metadata", "$.tier", ComparisonOperator.Equals, tier);
    }

    private sealed class SettledOrPricey : MySqlCriteria<Order>
    {
        public SettledOrPricey(string table, decimal min) : base(table) =>
            Group(LogicalOperator.Or, g => g.WhereEquals("status", OrderStatus.Settled).WhereGreaterThanOrEqual("total", min))
                .OrderByDescending("total").Limit(9);
    }

    private static Order NewOrder(Guid id, string description, OrderStatus status, int total = 0) => new() {
        Id = id, Description = description, Total = total, Status = status, CreatedOn = new DateOnly(2026, 1, 1)
    };

    public enum OrderStatus { Pending, Settled }

    /// <summary>
    ///     INSERT individual e bulk gravam enum e enum anulável exatamente da mesma forma (nome em PascalCase,
    ///     NULL preservado) e a leitura mapeia de volta.
    /// </summary>
    [Fact]
    public async Task Enum_e_enum_anulavel_gravam_igual_por_insert_e_por_bulk() {
        var table = $"shipments_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($"CREATE TABLE {table} (id CHAR(36) PRIMARY KEY, status VARCHAR(32) NOT NULL, previous_status VARCHAR(32) NULL)");
        var repo = _fixture.Repo<Shipment>(table);
        var single = new Shipment { Id = Guid.NewGuid(), Status = ShipmentStatus.InTransit, PreviousStatus = ShipmentStatus.Created };
        var bulk = new Shipment { Id = Guid.NewGuid(), Status = ShipmentStatus.InTransit, PreviousStatus = ShipmentStatus.Created };
        var bulkNull = new Shipment { Id = Guid.NewGuid(), Status = ShipmentStatus.Delivered, PreviousStatus = null };

        await repo.SaveAsync(single);
        await repo.BulkInsertAsync([bulk, bulkNull]);

        Task<string?> Raw(Guid id, string column) =>
            _fixture.ScalarAsync<string?>($"SELECT {column} FROM {table} WHERE id = '{id}'");
        (await Raw(single.Id, "status")).Should().Be("InTransit");
        (await Raw(bulk.Id, "status")).Should().Be("InTransit", "o bulk grava o mesmo nome que o INSERT");
        (await Raw(single.Id, "previous_status")).Should().Be("Created");
        (await Raw(bulk.Id, "previous_status")).Should().Be("Created");
        (await Raw(bulkNull.Id, "previous_status")).Should().BeNull();

        (await repo.GetByIdAsync(bulk.Id))!.PreviousStatus.Should().Be(ShipmentStatus.Created);
        (await repo.GetByIdAsync(bulkNull.Id))!.PreviousStatus.Should().BeNull();
        (await repo.GetByIdAsync(single.Id))!.Status.Should().Be(ShipmentStatus.InTransit);
    }

    public enum ShipmentStatus { Created, InTransit, Delivered }

    public sealed class Shipment
    {
        public Guid Id { get; set; }
        public ShipmentStatus Status { get; set; }
        public ShipmentStatus? PreviousStatus { get; set; }
    }

    public sealed class Order
    {
        public Guid Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public OrderStatus Status { get; set; }
        public DateOnly CreatedOn { get; set; }
    }

    public sealed class Metric
    {
        public Guid Id { get; set; }
        public decimal Value { get; set; }
        public long SourceSeq { get; set; }
        public DateTimeOffset ObservedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
    }

    /// <summary>Upsert condicional via <see cref="UpsertSpec" /> portável — o MESMO override dos outros providers.</summary>
    private sealed class SpecMetricRepository(IUnitOfWorkFactory factory, NamingStrategyResolver naming,
        IOptions<DatabaseOptions> options, MySqlBulkInserter inserter, string table)
        : MySqlRepository<Metric, Guid>(factory, NullLogger<MySqlRepository<Metric, Guid>>.Instance, naming,
            options, inserter, table)
    {
        protected override UpsertSpec? GetUpsertSpec() => UpsertSpec.OnKey("Id")
            .Preserve("CreatedAt")
            .SetServerUtcNow("UpdatedAt")
            .When(g => g.Newer("ObservedAt").OrGreater("SourceSeq").AndNotDeleted());
    }

    /// <summary>Repositório de teste cujo template de upsert vale para Save e bulk.</summary>
    private sealed class UpsertOrderRepository(IUnitOfWorkFactory factory,
        NamingStrategyResolver naming, IOptions<DatabaseOptions> options, MySqlBulkInserter inserter, string table)
        : MySqlRepository<Order, Guid>(factory, NullLogger<MySqlRepository<Order, Guid>>.Instance, naming,
            options, inserter, table)
    {
        protected override UpsertSpec? GetUpsertSpec() => UpsertSpec.OnKey("Id");
    }

    public sealed class RepoFixture : IAsyncLifetime
    {
        private readonly MySqlContainer _container = new MySqlBuilder()
            .WithImage("mysql:8.4")
            .Build();

        private ServiceProvider _provider = null!;

        public Task DisposeAsync() => Task.WhenAll(_provider.DisposeAsync().AsTask(), _container.DisposeAsync().AsTask());

        public async Task InitializeAsync() {
            await _container.StartAsync();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Database:ConnectionString"] = _container.GetConnectionString()
            }).Build();
            _provider = new ServiceCollection().AddLogging().AddAedisMySql(config).BuildServiceProvider();
        }

        public string ConnectionString => _container.GetConnectionString();

        public IUnitOfWorkFactory Factory => _provider.GetRequiredService<IUnitOfWorkFactory>();
        public NamingStrategyResolver Naming => _provider.GetRequiredService<NamingStrategyResolver>();
        public IOptions<DatabaseOptions> OptionsAccessor => _provider.GetRequiredService<IOptions<DatabaseOptions>>();
        public MySqlBulkInserter Inserter => _provider.GetRequiredService<MySqlBulkInserter>();

        public async Task ExecAsync(string sql) {
            await using var connection = new MySqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            await connection.ExecuteAsync(sql);
        }

        public async Task<string> CreateTableAsync() {
            var table = $"orders_{Guid.NewGuid():N}";
            await ExecAsync(
                $"CREATE TABLE {table} (id CHAR(36) PRIMARY KEY, description VARCHAR(255), total DECIMAL(18,2), status VARCHAR(32), created_on DATE)");
            return table;
        }

        public MySqlRepository<T, Guid> Repo<T>(string table, IAuditContext? audit = null) where T : class => new(
            Factory, NullLogger<MySqlRepository<T, Guid>>.Instance, Naming, OptionsAccessor, Inserter, table, audit);

        public MySqlRepository<Order, Guid> Plain(string table) => Repo<Order>(table);

        public MySqlRepository<Order, Guid> Hooked(string table) =>
            new HookedOrderRepository(Factory, Naming, OptionsAccessor, Inserter, table);

        public MySqlRepository<Order, Guid> Upsert(string table) =>
            new UpsertOrderRepository(Factory, Naming, OptionsAccessor, Inserter, table);

        public async Task<long> CountAsync(string table) => await ScalarAsync<long>($"SELECT count(*) FROM {table}");

        public async Task<T> ScalarAsync<T>(string sql) {
            await using var connection = new MySqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            return (await connection.ExecuteScalarAsync<T>(sql))!;
        }
    }
}
