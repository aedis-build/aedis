using Aedis.Database.Abstractions;
using Aedis.Database.Postgres;
using Aedis.Database.Postgres.Naming;
using Aedis.Database.Postgres.Queries;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
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

        // Save → insert
        await repo.SaveAsync(NewOrder(id, "v1", OrderStatus.Pending));
        (await repo.GetByIdAsync(id))!.Description.Should().Be("v1");

        // Save de novo (mesmo id) → o template faz upsert, não duplica
        await repo.SaveAsync(NewOrder(id, "v2", OrderStatus.Settled));
        var updated = await repo.GetByIdAsync(id);
        updated!.Description.Should().Be("v2");
        updated.Status.Should().Be(OrderStatus.Settled);

        // Bulk com os MESMOS ids → o mesmo template dirige o upsert em massa
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

        // Payload de injeção entra como VALOR (bind parameter) — é tratado como dado literal, sem efeito.
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

        public async Task<string> CreateTableAsync() {
            var table = $"orders_{Guid.NewGuid():N}";
            await using var connection = new NpgsqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                $"CREATE TABLE {table} (id uuid PRIMARY KEY, description text, total numeric, status text, created_on date)");
            return table;
        }

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
