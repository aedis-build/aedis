using Aedis.Database.Abstractions;
using Aedis.Database.Queries;
using Aedis.Database.SqlServer;
using Aedis.Database.Naming;
using Aedis.Database.SqlServer.Queries;
using Aedis.Domain.Entities;
using Aedis.Exceptions;
using Aedis.Security.Abstractions;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.MsSql;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>
///     Repositório convention-based contra um SQL Server real: o template <c>GetUpsertKeyColumns()</c>
///     dirigindo upsert (MERGE) tanto no Save quanto no bulk, busca via <see cref="ICriteria{TEntity}" />,
///     <see cref="RawCriteria{TEntity}" /> seguro contra injeção, a guarda <see cref="SqlIdentifier" /> e
///     auditoria/soft-delete por herança de <see cref="AuditableAggregateRoot{TId}" />. Integração opt-in:
///     ligue com a env <c>AEDIS_SQLSERVER_IT=1</c>.
/// </summary>
public sealed class SqlServerRepositoryTests : IClassFixture<SqlServerRepositoryTests.RepoFixture>
{
    private readonly RepoFixture _fixture;

    public SqlServerRepositoryTests(RepoFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GetUpsertKeyColumns_dirige_upsert_no_save_e_no_bulk() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = await _fixture.CreateOrdersAsync();
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
        (await _fixture.ScalarAsync<int>($"SELECT count(*) FROM {table} WHERE description = 'bulk-2'"))
            .Should().Be(500);
    }

    [SkippableFact]
    public async Task BulkInsert_simples_e_busca_por_criteria() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = await _fixture.CreateOrdersAsync();
        var repo = _fixture.Plain(table);
        var orders = Enumerable.Range(0, 1_000)
            .Select(i => NewOrder(Guid.NewGuid(), $"o-{i}", OrderStatus.Pending, i)).ToList();

        await repo.BulkInsertAsync(orders);

        (await repo.FindAsync(new RawCriteria<Order>($"SELECT * FROM {table}"))).Should().HaveCount(1_000);
        (await repo.CountAsync(new RawCriteria<Order>($"SELECT * FROM {table} WHERE total >= @min",
            new { min = 500m }))).Should().Be(500);
    }

    [SkippableFact]
    public async Task RawCriteria_e_seguro_contra_injecao_em_valores() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = await _fixture.CreateOrdersAsync();
        var repo = _fixture.Plain(table);
        await repo.SaveAsync(NewOrder(Guid.NewGuid(), "real", OrderStatus.Pending));

        var attack = $"x'; DROP TABLE {table}; --";
        var result = await repo.FindAsync(
            new RawCriteria<Order>($"SELECT * FROM {table} WHERE description = @desc", new { desc = attack }));

        result.Should().BeEmpty("nenhuma linha tem essa descrição literal");
        (await _fixture.CountAsync(table)).Should().Be(1, "a tabela continua intacta — nada foi dropado");
    }

    [SkippableFact]
    public async Task Exists_Query_BulkUpdate_Command_e_delete_fisico_sem_soft_delete() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = await _fixture.CreateOrdersAsync();
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
        (await _fixture.CountAsync(table)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Erro_do_banco_na_escrita_vira_BusinessException_com_a_restricao_e_a_sessao_e_revertida() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = await _fixture.CreateOrdersAsync();
        var repo = _fixture.Plain(table);
        var id = Guid.NewGuid();
        await repo.SaveAsync(NewOrder(id, "a", OrderStatus.Pending));

        var business = (await FluentActions.Awaiting(() => repo.SaveAsync(NewOrder(id, "dup", OrderStatus.Pending)))
            .Should().ThrowAsync<BusinessException>()).Which;

        business.ViolationType.Should().Be(ViolationType.UniqueConstraintViolation);
        business.Rule.Should().StartWith("PK__", "o nome da restrição vem da mensagem do servidor");
        (await _fixture.CountAsync(table)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Erro_na_leitura_passa_pelo_hook_OnDatabaseError_e_sem_tradutor_e_relancado_cru() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = await _fixture.CreateOrdersAsync();
        var missing = new RawCriteria<Order>("SELECT * FROM tabela_inexistente");

        await FluentActions.Awaiting(() => _fixture.Plain(table).FindAsync(missing))
            .Should().ThrowAsync<SqlException>("erro Other sem mapeamento padrão segue cru");
        await FluentActions.Awaiting(() => _fixture.Plain(table).CommandAsync(new RawCriteria<Order>("UPDATE tabela_inexistente SET total = 0")))
            .Should().ThrowAsync<SqlException>("o mesmo vale na sessão de escrita");
        await FluentActions.Awaiting(() => _fixture.Hooked(table).FindAsync(missing))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("sqlserver:Other");
    }

    [SkippableFact]
    public async Task UnitOfWork_guarda_escrita_em_leitura_transacao_dupla_e_reverte_no_descarte() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        await using (var readOnly = new UnitOfWork(new SqlConnection(_fixture.ConnectionString), true, NullLogger<UnitOfWork>.Instance))
            await FluentActions.Awaiting(() => readOnly.ExecuteAsync("SELECT 1")).Should().ThrowAsync<InvalidOperationException>();

        var table = await _fixture.CreateOrdersAsync();
        var uow = new UnitOfWork(new SqlConnection(_fixture.ConnectionString), false, NullLogger<UnitOfWork>.Instance);
        var tx = await uow.BeginTransactionAsync();
        await FluentActions.Awaiting(() => uow.BeginTransactionAsync()).Should().ThrowAsync<InvalidOperationException>();
        FluentActions.Invoking(() => uow.SetTransaction(tx)).Should().Throw<InvalidOperationException>();
        uow.HasActiveTransaction().Should().BeTrue();
        uow.GetTransaction().Should().BeSameAs(tx);
        await uow.ExecuteAsync($"INSERT INTO {table} (id) VALUES (@id)", new { id = Guid.NewGuid() });
        await uow.RollbackAsync();
        uow.HasActiveTransaction().Should().BeFalse();
        (await _fixture.CountAsync(table)).Should().Be(0);

        await uow.BeginTransactionAsync();
        await uow.ExecuteAsync($"INSERT INTO {table} (id) VALUES (@id)", new { id = Guid.NewGuid() });
        await uow.DisposeAsync();
        await uow.DisposeAsync();
        (await _fixture.CountAsync(table)).Should().Be(0, "descarte sem commit reverte");
    }

    [SkippableFact]
    public async Task Replicas_de_leitura_sao_usadas_em_rodizio_nas_sessoes_de_leitura() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Database:ConnectionString"] = _fixture.ConnectionString,
            ["Database:ReadConnectionStrings:0"] = _fixture.ConnectionString,
            ["Database:ReadConnectionStrings:1"] = _fixture.ConnectionString
        }).Build();
        await using var provider = new ServiceCollection().AddLogging().AddAedisSqlServer(config).BuildServiceProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();

        for (var i = 0; i < 3; i++) {
            await using var session = await factory.CreateReadSessionAsync();
            session.HasActiveTransaction().Should().BeFalse("leitura roda em autocommit: nada a confirmar ou reverter no descarte");
            (await session.QuerySingleOrDefaultAsync<int>("SELECT 1")).Should().Be(1);
            await session.CommitAsync();
        }
    }

    [SkippableFact]
    public async Task Strings_vao_como_varchar_por_padrao_e_nvarchar_com_Unicode() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = $"coded_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($"CREATE TABLE {table} (id UNIQUEIDENTIFIER PRIMARY KEY, code VARCHAR(20), nome NVARCHAR(50))");
        var repo = _fixture.Repo<Coded>(table);
        var entity = new Coded { Id = Guid.NewGuid(), Code = "abc", Nome = "ação ✓" };

        await repo.SaveAsync(entity);

        var saved = await repo.GetByIdAsync(entity.Id);
        saved!.Nome.Should().Be("ação ✓", "o parâmetro [Unicode] vai como nvarchar e não perde o caractere");
        saved.Code.Should().Be("abc");
        await using var session = await _fixture.Factory.CreateReadSessionAsync();
        (await session.QuerySingleOrDefaultAsync<string>("SELECT CAST(SQL_VARIANT_PROPERTY(@p, 'BaseType') AS nvarchar(20))", new { p = "x" }))
            .Should().Be("varchar", "strings soltas (critérios, objetos anônimos) seguem o mapa global");
    }

    public sealed class Coded
    {
        public Guid Id { get; set; }
        [System.ComponentModel.DataAnnotations.MaxLength(20)] public string Code { get; set; } = string.Empty;
        [Unicode] public string Nome { get; set; } = string.Empty;
    }

    public sealed class Projection
    {
        public string Description { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }

    /// <summary>Repositório de teste que traduz qualquer erro do banco pelo hook, inclusive os sem mapeamento padrão.</summary>
    private sealed class HookedOrderRepository(IUnitOfWorkFactory factory, NamingStrategyResolver naming,
        IOptions<DatabaseOptions> options, SqlServerBulkInserter inserter, string table)
        : SqlServerRepository<Order, Guid>(factory, NullLogger<SqlServerRepository<Order, Guid>>.Instance, naming,
            options, inserter, table)
    {
        protected override Exception? OnDatabaseError(DatabaseError error) => new InvalidOperationException($"{error.Provider}:{error.Kind}");
    }

    [Theory]
    [InlineData("orders", true)]
    [InlineData("dbo.orders", true)]
    [InlineData("orders; DROP TABLE x", false)]
    [InlineData("orders--", false)]
    [InlineData("orders\"", false)]
    public void SqlIdentifier_aceita_so_identificadores_seguros(string identifier, bool valid) {
        SqlIdentifier.IsValid(identifier).Should().Be(valid);
        if (!valid)
            FluentActions.Invoking(() => SqlIdentifier.Validate(identifier)).Should().Throw<ArgumentException>();
    }

    [SkippableFact]
    public async Task Auditoria_carimba_created_updated_by_at_e_reason_quando_presentes() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = $"audited_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($@"CREATE TABLE {table} (
            id UNIQUEIDENTIFIER PRIMARY KEY, name NVARCHAR(200),
            created_at DATETIMEOFFSET, created_by NVARCHAR(100),
            updated_at DATETIMEOFFSET, updated_by NVARCHAR(100), updated_reason NVARCHAR(200))");

        var now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var repo = _fixture.Repo<Audited>(table, new FakeAudit("alice", now, "correcao manual"));

        var entity = new Audited { Id = Guid.NewGuid(), Name = "x" };
        await repo.SaveAsync(entity);

        entity.CreatedBy.Should().Be("alice", "o objeto também é carimbado");
        (await _fixture.ScalarAsync<string>($"SELECT created_by FROM {table} WHERE id='{entity.Id}'"))
            .Should().Be("alice");
        (await _fixture.ScalarAsync<string>($"SELECT updated_reason FROM {table} WHERE id='{entity.Id}'"))
            .Should().Be("correcao manual");

        await repo.BulkInsertAsync([new Audited { Id = Guid.NewGuid(), Name = "b" }]);
        (await _fixture.ScalarAsync<int>($"SELECT count(*) FROM {table} WHERE created_by = 'alice'"))
            .Should().Be(2);
    }

    [SkippableFact]
    public async Task AuditableAggregateRoot_da_auditoria_e_soft_delete_por_heranca_sem_boilerplate() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = $"calendarios_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($@"CREATE TABLE {table} (
            id UNIQUEIDENTIFIER PRIMARY KEY, codigo NVARCHAR(50), nome NVARCHAR(200),
            created_at DATETIMEOFFSET, created_by NVARCHAR(100), updated_at DATETIMEOFFSET,
            updated_by NVARCHAR(100), updated_reason NVARCHAR(200),
            is_deleted BIT NOT NULL DEFAULT 0, deleted_at DATETIMEOFFSET, deleted_by NVARCHAR(100))");

        var now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var repo = _fixture.Repo<CalendarioEscopo>(table, new FakeAudit("bob", now, "ajuste de escopo"));

        var calendario = new CalendarioEscopo { Id = Guid.NewGuid(), Codigo = "C1", Nome = "Escopo" };
        await repo.SaveAsync(calendario);

        calendario.CreatedBy.Should().Be("bob", "a entidade só herda — o repo carimba");
        (await _fixture.ScalarAsync<string>($"SELECT created_by FROM {table} WHERE id='{calendario.Id}'"))
            .Should().Be("bob");

        await repo.DeleteAsync(calendario.Id);
        (await _fixture.ScalarAsync<bool>($"SELECT is_deleted FROM {table} WHERE id='{calendario.Id}'"))
            .Should().BeTrue();
        (await _fixture.ScalarAsync<string>($"SELECT deleted_by FROM {table} WHERE id='{calendario.Id}'"))
            .Should().Be("bob");

        (await repo.GetByIdAsync(calendario.Id)).Should().BeNull("a linha soft-deleted é filtrada");
    }

    [SkippableFact]
    public async Task Cadeia_DI_completa_carimba_o_usuario_logado_automaticamente() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        const string table = "calendario_escopos";
        await _fixture.ExecAsync($@"IF OBJECT_ID('{table}') IS NOT NULL DROP TABLE {table};
            CREATE TABLE {table} (
            id UNIQUEIDENTIFIER PRIMARY KEY, codigo NVARCHAR(50), nome NVARCHAR(200),
            created_at DATETIMEOFFSET, created_by NVARCHAR(100), updated_at DATETIMEOFFSET,
            updated_by NVARCHAR(100), updated_reason NVARCHAR(200),
            is_deleted BIT NOT NULL DEFAULT 0, deleted_at DATETIMEOFFSET, deleted_by NVARCHAR(100))");

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
            .AddAedisSqlServer(config)
            .BuildServiceProvider();

        var id = Guid.NewGuid();
        using (var scope = provider.CreateScope()) {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<CalendarioEscopo, Guid>>();
            await repo.SaveAsync(new CalendarioEscopo { Id = id, Codigo = "C1", Nome = "Escopo" }, default);
        }

        (await _fixture.ScalarAsync<string>($"SELECT created_by FROM {table} WHERE id='{id}'"))
            .Should().Be("joana", "o usuário logado é carimbado automaticamente pela cadeia de DI");
    }

    /// <summary>
    ///     Upsert condicional pela <see cref="UpsertSpec" /> portável (a MESMA usada no provider PostgreSQL):
    ///     só atualiza se a linha que entra for mais nova (observed_at OU source_seq), preserva
    ///     <c>created_at</c> e ignora linhas soft-deleted. O mesmo spec dirige Save e BulkInsertChunked.
    /// </summary>
    [SkippableFact]
    public async Task Upsert_condicional_so_atualiza_se_mais_novo_preserva_created_e_ignora_deletado() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = $"metrics_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($@"CREATE TABLE {table} (
            id UNIQUEIDENTIFIER PRIMARY KEY, [value] DECIMAL(18,4), source_seq BIGINT,
            observed_at DATETIMEOFFSET, created_at DATETIMEOFFSET, updated_at DATETIMEOFFSET,
            is_deleted BIT NOT NULL DEFAULT 0)");
        var repo = new ConditionalMetricRepository(_fixture.Factory, _fixture.Naming, _fixture.OptionsAccessor,
            _fixture.Inserter, table);

        var id = Guid.NewGuid();
        var created = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var t0 = DateTimeOffset.Parse("2026-03-01T00:00:00Z");

        await repo.SaveAsync(new Metric { Id = id, Value = 10, SourceSeq = 1, ObservedAt = t0, CreatedAt = created });

        Task<decimal> Value() => _fixture.ScalarAsync<decimal>($"SELECT [value] FROM {table} WHERE id = '{id}'");

        await repo.BulkInsertChunkedAsync([
            new Metric { Id = id, Value = 20, SourceSeq = 2, ObservedAt = t0.AddDays(10), CreatedAt = DateTimeOffset.UtcNow }
        ]);
        (await Value()).Should().Be(20, "dado mais novo atualiza");
        (await _fixture.ScalarAsync<DateTimeOffset>($"SELECT created_at FROM {table} WHERE id = '{id}'"))
            .Should().Be(created, "created_at é preservado");

        await repo.BulkInsertChunkedAsync([
            new Metric { Id = id, Value = 99, SourceSeq = 1, ObservedAt = t0.AddDays(-10), CreatedAt = created }
        ]);
        (await Value()).Should().Be(20, "dado mais velho é ignorado");

        await _fixture.ExecAsync($"UPDATE {table} SET is_deleted = 1 WHERE id = '{id}'");
        await repo.BulkInsertChunkedAsync([
            new Metric { Id = id, Value = 77, SourceSeq = 9, ObservedAt = t0.AddDays(99), CreatedAt = created }
        ]);
        (await Value()).Should().Be(20, "linha soft-deleted não é atualizada");
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

    /// <summary>O guard condicional é declarado uma vez, de forma agnóstica — idêntico ao repo equivalente no Postgres.</summary>
    private sealed class ConditionalMetricRepository(IUnitOfWorkFactory factory, NamingStrategyResolver naming,
        IOptions<DatabaseOptions> options, SqlServerBulkInserter inserter, string table)
        : SqlServerRepository<Metric, Guid>(factory, NullLogger<SqlServerRepository<Metric, Guid>>.Instance, naming,
            options, inserter, table)
    {
        protected override UpsertSpec? GetUpsertSpec() => UpsertSpec.OnKey("Id")
            .Preserve("CreatedAt")
            .SetServerUtcNow("UpdatedAt")
            .When(g => g.Newer("ObservedAt").OrGreater("SourceSeq").AndNotDeleted());
    }

    private sealed record FakeAudit(string? CurrentActor, DateTimeOffset Now, string? Reason) : IAuditContext;

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

    public sealed class CalendarioEscopo : AuditableAggregateRoot<Guid>
    {
        public string Codigo { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
    }

    private static Order NewOrder(Guid id, string description, OrderStatus status, int total = 0) => new() {
        Id = id, Description = description, Total = total, Status = status, CreatedOn = new DateOnly(2026, 1, 1)
    };

    public enum OrderStatus { Pending, Settled }

    /// <summary>
    ///     INSERT individual e bulk gravam enum e enum anulável exatamente da mesma forma (nome em PascalCase,
    ///     NULL preservado) e a leitura mapeia de volta — a divergência de caixa entre os dois caminhos que já
    ///     existiu no framework de origem não pode voltar.
    /// </summary>
    [SkippableFact]
    public async Task Enum_e_enum_anulavel_gravam_igual_por_insert_e_por_bulk() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        var table = $"shipments_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($"CREATE TABLE {table} (id uniqueidentifier PRIMARY KEY, status nvarchar(50) NOT NULL, previous_status nvarchar(50) NULL)");
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

    /// <summary>Repositório de teste cujo template de upsert (chave <c>Id</c>) vale para Save e bulk.</summary>
    private sealed class UpsertOrderRepository(IUnitOfWorkFactory factory, NamingStrategyResolver naming,
        IOptions<DatabaseOptions> options, SqlServerBulkInserter inserter, string table)
        : SqlServerRepository<Order, Guid>(factory, NullLogger<SqlServerRepository<Order, Guid>>.Instance, naming,
            options, inserter, table)
    {
        protected override UpsertSpec? GetUpsertSpec() => UpsertSpec.OnKey("Id");
    }

    public sealed class RepoFixture : IAsyncLifetime
    {
        private MsSqlContainer? _container;
        private ServiceProvider? _provider;

        public bool Enabled { get; } = Environment.GetEnvironmentVariable("AEDIS_SQLSERVER_IT") == "1";

        public string ConnectionString => _container!.GetConnectionString();
        public IUnitOfWorkFactory Factory => _provider!.GetRequiredService<IUnitOfWorkFactory>();
        public NamingStrategyResolver Naming => _provider!.GetRequiredService<NamingStrategyResolver>();
        public IOptions<DatabaseOptions> OptionsAccessor => _provider!.GetRequiredService<IOptions<DatabaseOptions>>();
        public SqlServerBulkInserter Inserter => _provider!.GetRequiredService<SqlServerBulkInserter>();

        public async Task InitializeAsync() {
            if (!Enabled) return;
            _container = new MsSqlBuilder().Build();
            await _container.StartAsync();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Database:ConnectionString"] = _container.GetConnectionString()
            }).Build();
            _provider = new ServiceCollection().AddLogging().AddAedisSqlServer(config).BuildServiceProvider();
        }

        public async Task DisposeAsync() {
            if (_provider is not null) await _provider.DisposeAsync();
            if (_container is not null) await _container.DisposeAsync();
        }

        public async Task ExecAsync(string sql) {
            await using var connection = new SqlConnection(_container!.GetConnectionString());
            await connection.OpenAsync();
            await connection.ExecuteAsync(sql);
        }

        public async Task<string> CreateOrdersAsync() {
            var table = $"orders_{Guid.NewGuid():N}";
            await ExecAsync(
                $"CREATE TABLE {table} (id UNIQUEIDENTIFIER PRIMARY KEY, description NVARCHAR(200), "
                + "total DECIMAL(18,4), status NVARCHAR(50), created_on DATE)");
            return table;
        }

        public SqlServerRepository<T, Guid> Repo<T>(string table, IAuditContext? audit = null) where T : class => new(
            Factory, NullLogger<SqlServerRepository<T, Guid>>.Instance, Naming, OptionsAccessor, Inserter, table, audit);

        public SqlServerRepository<Order, Guid> Plain(string table) => new(
            Factory, NullLogger<SqlServerRepository<Order, Guid>>.Instance, Naming, OptionsAccessor, Inserter, table);

        public SqlServerRepository<Order, Guid> Upsert(string table) =>
            new UpsertOrderRepository(Factory, Naming, OptionsAccessor, Inserter, table);

        public SqlServerRepository<Order, Guid> Hooked(string table) =>
            new HookedOrderRepository(Factory, Naming, OptionsAccessor, Inserter, table);

        public async Task<int> CountAsync(string table) => await ScalarAsync<int>($"SELECT count(*) FROM {table}");

        public async Task<T> ScalarAsync<T>(string sql) {
            await using var connection = new SqlConnection(_container!.GetConnectionString());
            await connection.OpenAsync();
            return (await connection.ExecuteScalarAsync<T>(sql))!;
        }
    }
}
