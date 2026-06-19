using Aedis.Database.Abstractions;
using Aedis.Database.Postgres;
using Aedis.Database.Postgres.Naming;
using Aedis.Database.Postgres.Queries;
using Aedis.Domain.Entities;
using Aedis.Security.Abstractions;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace Aedis.Database.Postgres.Tests;

/// <summary>
///     Repositório convention-based contra um PostgreSQL real: o template <c>GetOnConflictClause()</c>
///     dirigindo upsert tanto no Save quanto no bulk, busca via <see cref="ICriteria{TEntity}" />,
///     <see cref="RawCriteria{TEntity}" /> seguro contra injeção e a guarda <see cref="SqlIdentifier" />.
/// </summary>
public sealed class PostgresRepositoryTests : IClassFixture<PostgresRepositoryTests.RepoFixture>
{
    private readonly RepoFixture _fixture;

    public PostgresRepositoryTests(RepoFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetOnConflictClause_dirige_upsert_no_save_e_no_bulk() {
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
    }

    [Fact]
    public async Task RawCriteria_e_seguro_contra_injecao_em_valores() {
        var table = await _fixture.CreateTableAsync();
        var repo = _fixture.Plain(table);
        await repo.SaveAsync(NewOrder(Guid.NewGuid(), "real", OrderStatus.Pending));

        var attack = $"x'; DROP TABLE {table}; --";
        var result = await repo.FindAsync(
            new RawCriteria<Order>($"SELECT * FROM {table} WHERE description = @desc", new { desc = attack }));

        result.Should().BeEmpty("nenhuma linha tem essa descrição literal");
        (await _fixture.CountAsync(table)).Should().Be(1, "a tabela continua intacta — nada foi dropado");
    }

    [Theory]
    [InlineData("orders", true)]
    [InlineData("public.orders", true)]
    [InlineData("orders; DROP TABLE x", false)]
    [InlineData("orders--", false)]
    [InlineData("orders\"", false)]
    public void SqlIdentifier_aceita_so_identificadores_seguros(string identifier, bool valid) {
        SqlIdentifier.IsValid(identifier).Should().Be(valid);
        if (!valid)
            FluentActions.Invoking(() => SqlIdentifier.Validate(identifier)).Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Upsert condicional com guard (só atualiza
    ///     se o dado de entrada for mais novo — timestamp OU sequencial), preservando <c>created_at</c> e
    ///     ignorando linhas soft-deleted. O mesmo template dirige Save e BulkInsertChunked.
    /// </summary>
    [Fact]
    public async Task Upsert_condicional_so_atualiza_se_mais_novo_preserva_created_e_ignora_deletado() {
        var table = $"metrics_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($@"CREATE TABLE {table} (
            id uuid PRIMARY KEY, value numeric, source_seq bigint,
            observed_at timestamptz, created_at timestamptz, updated_at timestamptz,
            is_deleted boolean NOT NULL DEFAULT false)");
        var repo = new ConditionalMetricRepository(_fixture.Factory, _fixture.Naming, _fixture.OptionsAccessor,
            _fixture.Inserter, table);

        var id = Guid.NewGuid();
        var created = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var t0 = DateTimeOffset.Parse("2026-03-01T00:00:00Z");

        await repo.SaveAsync(new Metric { Id = id, Value = 10, SourceSeq = 1, ObservedAt = t0, CreatedAt = created });

        Task<decimal> Value() => _fixture.ScalarAsync<decimal>($"SELECT value FROM {table} WHERE id = '{id}'");

        await repo.BulkInsertChunkedAsync([
            new Metric { Id = id, Value = 20, SourceSeq = 2, ObservedAt = t0.AddDays(10), CreatedAt = DateTimeOffset.UtcNow }
        ]);
        (await Value()).Should().Be(20, "dado mais novo atualiza");
        (await _fixture.ScalarAsync<DateTime>($"SELECT created_at FROM {table} WHERE id = '{id}'"))
            .Should().Be(created.UtcDateTime, "created_at é preservado");

        await repo.BulkInsertChunkedAsync([
            new Metric { Id = id, Value = 99, SourceSeq = 1, ObservedAt = t0.AddDays(-10), CreatedAt = created }
        ]);
        (await Value()).Should().Be(20, "dado mais velho é ignorado");

        await _fixture.ExecAsync($"UPDATE {table} SET is_deleted = true WHERE id = '{id}'");
        await repo.BulkInsertChunkedAsync([
            new Metric { Id = id, Value = 77, SourceSeq = 9, ObservedAt = t0.AddDays(99), CreatedAt = created }
        ]);
        (await Value()).Should().Be(20, "linha soft-deleted não é atualizada");
    }

    [Fact]
    public async Task Arrays_postgres_em_save_bulk_e_query_com_operadores_gin() {
        var table = $"tagged_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($"CREATE TABLE {table} (id uuid PRIMARY KEY, tags text[])");
        var repo = _fixture.Repo<Tagged>(table);

        await repo.SaveAsync(new Tagged { Id = Guid.NewGuid(), Tags = ["vip", "gold"] });
        await repo.BulkInsertAsync([
            new Tagged { Id = Guid.NewGuid(), Tags = ["gold"] },
            new Tagged { Id = Guid.NewGuid(), Tags = ["silver"] }
        ]);

        (await repo.FindAsync(new TagsOverlap(table, ["vip", "silver"]))).Should().HaveCount(2);

        (await repo.FindAsync(new TagsEqualsAny(table, "gold"))).Should().HaveCount(2);
    }

    [Fact]
    public async Task Auditoria_carimba_created_updated_by_at_e_reason_quando_presentes() {
        var table = $"audited_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($@"CREATE TABLE {table} (
            id uuid PRIMARY KEY, name text,
            created_at timestamptz, created_by text,
            updated_at timestamptz, updated_by text, updated_reason text)");

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
            id uuid PRIMARY KEY, name text,
            created_at timestamptz, created_by text,
            updated_at timestamptz, updated_by text, updated_reason text)");

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
        await _fixture.ExecAsync($@"CREATE TABLE {table} (
            id uuid PRIMARY KEY, codigo text, nome text,
            created_at timestamptz, created_by text, updated_at timestamptz, updated_by text, updated_reason text,
            is_deleted boolean NOT NULL DEFAULT false, deleted_at timestamptz, deleted_by text)");

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
    }

    public sealed class CalendarioEscopo : AuditableAggregateRoot<Guid>
    {
        public string Codigo { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
    }

    /// <summary>
    ///     Contrato: herdar de AuditableAggregateRoot é o opt-in; criar as colunas na migration é
    ///     responsabilidade do app. Sem schema generator — se faltar uma coluna (aqui, a tabela é criada
    ///     sem <c>updated_reason</c>, que o AuditableAggregateRoot possui), o Save falha na hora com erro
    ///     explícito do PostgreSQL (42703 undefined_column), em vez de qualquer DDL automático.
    /// </summary>
    [Fact]
    public async Task Coluna_de_auditoria_ausente_no_schema_falha_claro_no_save() {
        var table = $"incompleto_{Guid.NewGuid():N}";
        await _fixture.ExecAsync($@"CREATE TABLE {table} (
            id uuid PRIMARY KEY, codigo text, nome text,
            created_at timestamptz, created_by text, updated_at timestamptz, updated_by text,
            is_deleted boolean NOT NULL DEFAULT false, deleted_at timestamptz, deleted_by text)");
        var repo = _fixture.Repo<CalendarioEscopo>(table, new FakeAudit("bob", DateTimeOffset.UtcNow, "motivo"));

        var act = async () =>
            await repo.SaveAsync(new CalendarioEscopo { Id = Guid.NewGuid(), Codigo = "C", Nome = "N" });

        var exception = (await act.Should().ThrowAsync<PostgresException>()).Which;
        exception.SqlState.Should().Be("42703", "undefined_column — fail-fast, sem DDL automático");
        exception.Message.Should().Contain("updated_reason");
    }

    [Fact]
    public async Task Cadeia_DI_completa_carimba_o_usuario_logado_automaticamente() {
        var table = "calendario_escopos";
        await _fixture.ExecAsync($@"CREATE TABLE IF NOT EXISTS {table} (
            id uuid PRIMARY KEY, codigo text, nome text,
            created_at timestamptz, created_by text, updated_at timestamptz, updated_by text, updated_reason text,
            is_deleted boolean NOT NULL DEFAULT false, deleted_at timestamptz, deleted_by text)");

        var user = NSubstitute.Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.Id.Returns("joana");

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Database:ConnectionString"] = _fixture.ConnectionString
        }).Build();

        var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(user)
            .AddAedisAuditContext()
            .AddAedisPostgres(config)
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

    private sealed record FakeAudit(string? CurrentActor, DateTimeOffset Now, string? Reason) : IAuditContext;

    public sealed class Tagged
    {
        public Guid Id { get; set; }
        public string[] Tags { get; set; } = [];
    }

    private sealed class TagsOverlap : PostgresCriteria<Tagged>
    {
        public TagsOverlap(string table, string[] values) : base(table) => WhereArrayOverlap("tags", values);
    }

    private sealed class TagsEqualsAny : PostgresCriteria<Tagged>
    {
        public TagsEqualsAny(string table, string value) : base(table) => WhereEqualsAny("tags", value);
    }

    private static Order NewOrder(Guid id, string description, OrderStatus status, int total = 0) => new() {
        Id = id, Description = description, Total = total, Status = status, CreatedOn = new DateOnly(2026, 1, 1)
    };

    public enum OrderStatus { Pending, Settled }

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

    /// <summary>
    ///     Upsert condicional equivalente ao caso real da tabela <c>urs</c>: só atualiza se o dado de
    ///     entrada for mais novo (observed_at OU source_seq), preserva <c>created_at</c> e ignora linhas
    ///     soft-deleted. <c>updated_at = now()</c>. O guard referencia a tabela alvo por <c>TableName</c>.
    /// </summary>
    private sealed class ConditionalMetricRepository(IUnitOfWorkFactory factory, NamingStrategyResolver naming,
        IOptions<DatabaseOptions> options, PostgresBulkInserter inserter, string table)
        : PostgresRepository<Metric, Guid>(factory, NullLogger<PostgresRepository<Metric, Guid>>.Instance, naming,
            options, inserter, table)
    {
        protected override string? GetOnConflictClause() => $@"
            ON CONFLICT (id) DO UPDATE SET
                value       = EXCLUDED.value,
                source_seq  = EXCLUDED.source_seq,
                observed_at = EXCLUDED.observed_at,
                updated_at  = now()
            WHERE (
                COALESCE(EXCLUDED.observed_at, 'infinity'::timestamptz)
                    >= COALESCE({TableName}.observed_at, '-infinity'::timestamptz)
                OR
                COALESCE(EXCLUDED.source_seq, 9223372036854775807)
                    > COALESCE({TableName}.source_seq, -9223372036854775808)
            )
            AND {TableName}.is_deleted = false";
    }

    /// <summary>Repositório de teste cujo template de upsert vale para Save e bulk.</summary>
    private sealed class UpsertOrderRepository(IUnitOfWorkFactory factory,
        NamingStrategyResolver naming, IOptions<DatabaseOptions> options, PostgresBulkInserter inserter, string table)
        : PostgresRepository<Order, Guid>(factory, NullLogger<PostgresRepository<Order, Guid>>.Instance, naming,
            options, inserter, table)
    {
        protected override string? GetOnConflictClause() => BuildUpsertClause("Id");
    }

    public sealed class RepoFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        private ServiceProvider _provider = null!;

        public Task DisposeAsync() => Task.WhenAll(_provider.DisposeAsync().AsTask(), _container.DisposeAsync().AsTask());

        public async Task InitializeAsync() {
            await _container.StartAsync();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Database:ConnectionString"] = _container.GetConnectionString()
            }).Build();
            _provider = new ServiceCollection().AddLogging().AddAedisPostgres(config).BuildServiceProvider();
        }

        public string ConnectionString => _container.GetConnectionString();

        public IUnitOfWorkFactory Factory => _provider.GetRequiredService<IUnitOfWorkFactory>();
        public NamingStrategyResolver Naming => _provider.GetRequiredService<NamingStrategyResolver>();
        public IOptions<DatabaseOptions> OptionsAccessor => _provider.GetRequiredService<IOptions<DatabaseOptions>>();
        public PostgresBulkInserter Inserter => _provider.GetRequiredService<PostgresBulkInserter>();

        public async Task ExecAsync(string sql) {
            await using var connection = new NpgsqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            await connection.ExecuteAsync(sql);
        }

        public async Task<string> CreateTableAsync() {
            var table = $"orders_{Guid.NewGuid():N}";
            await using var connection = new NpgsqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                $"CREATE TABLE {table} (id uuid PRIMARY KEY, description text, total numeric, status text, created_on date)");
            return table;
        }

        public PostgresRepository<T, Guid> Repo<T>(string table, IAuditContext? audit = null) where T : class => new(
            _provider.GetRequiredService<IUnitOfWorkFactory>(),
            NullLogger<PostgresRepository<T, Guid>>.Instance,
            _provider.GetRequiredService<NamingStrategyResolver>(),
            _provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            _provider.GetRequiredService<PostgresBulkInserter>(),
            table, audit);

        public PostgresRepository<Order, Guid> Plain(string table) => new(
            _provider.GetRequiredService<IUnitOfWorkFactory>(),
            NullLogger<PostgresRepository<Order, Guid>>.Instance,
            _provider.GetRequiredService<NamingStrategyResolver>(),
            _provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            _provider.GetRequiredService<PostgresBulkInserter>(),
            table);

        public PostgresRepository<Order, Guid> Upsert(string table) => new UpsertOrderRepository(
            _provider.GetRequiredService<IUnitOfWorkFactory>(),
            _provider.GetRequiredService<NamingStrategyResolver>(),
            _provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            _provider.GetRequiredService<PostgresBulkInserter>(),
            table);

        public async Task<long> CountAsync(string table) => await ScalarAsync<long>($"SELECT count(*) FROM {table}");

        public async Task<T> ScalarAsync<T>(string sql) {
            await using var connection = new NpgsqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            return (await connection.ExecuteScalarAsync<T>(sql))!;
        }
    }
}
