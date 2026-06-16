using Aedis.Database.Abstractions;
using Aedis.Database.Postgres;
using Aedis.Database.Postgres.Naming;
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
///     Repositório convention-based contra um PostgreSQL real: upsert (Save), GetById/Exists, busca por
///     <see cref="ICriteria{TEntity}" />, bulk insert e delete — com enums persistidos como string e
///     colunas snake_case mapeadas de volta para as propriedades.
/// </summary>
public sealed class PostgresRepositoryTests : IClassFixture<PostgresRepositoryTests.RepoFixture>
{
    private readonly RepoFixture _fixture;

    public PostgresRepositoryTests(RepoFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Save_faz_upsert_e_GetById_reconstroi_a_entidade() {
        var (repo, _) = await _fixture.NewRepoAsync();
        var order = new Order {
            Id = Guid.NewGuid(), Description = "primeira", Total = 10.5m,
            Status = OrderStatus.Pending, CreatedOn = new DateOnly(2026, 1, 10)
        };

        await repo.SaveAsync(order);
        var loaded = await repo.GetByIdAsync(order.Id);

        loaded.Should().NotBeNull();
        loaded!.Description.Should().Be("primeira");
        loaded.Total.Should().Be(10.5m);
        loaded.Status.Should().Be(OrderStatus.Pending);
        loaded.CreatedOn.Should().Be(new DateOnly(2026, 1, 10));

        order.Description = "atualizada";
        order.Status = OrderStatus.Settled;
        await repo.SaveAsync(order);

        var updated = await repo.GetByIdAsync(order.Id);
        updated!.Description.Should().Be("atualizada");
        updated.Status.Should().Be(OrderStatus.Settled);
        (await repo.ExistsAsync(order.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task BulkInsert_e_busca_por_criteria() {
        var (repo, table) = await _fixture.NewRepoAsync();
        var orders = Enumerable.Range(0, 1_000).Select(i => new Order {
            Id = Guid.NewGuid(), Description = $"o-{i}", Total = i, Status = OrderStatus.Pending,
            CreatedOn = new DateOnly(2026, 1, 1)
        }).ToList();

        await repo.BulkInsertAsync(orders);

        var all = await repo.FindAsync(new RawCriteria<Order>($"SELECT * FROM {table}"));
        all.Should().HaveCount(1_000);
        (await repo.CountAsync(new RawCriteria<Order>($"SELECT * FROM {table} WHERE total >= 500"))).Should().Be(500);
    }

    [Fact]
    public async Task Delete_remove_a_entidade() {
        var (repo, _) = await _fixture.NewRepoAsync();
        var order = new Order { Id = Guid.NewGuid(), Description = "x", Total = 1, Status = OrderStatus.Pending };

        await repo.SaveAsync(order);
        await repo.DeleteAsync(order.Id);

        (await repo.GetByIdAsync(order.Id)).Should().BeNull();
    }

    public enum OrderStatus { Pending, Settled }

    public sealed class Order
    {
        public Guid Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public OrderStatus Status { get; set; }
        public DateOnly CreatedOn { get; set; }
    }

    private sealed class RawCriteria<T>(string sql) : ICriteria<T>
    {
        public (string Sql, object Parameters) Build() => (sql, new { });
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

        public async Task<(PostgresRepository<Order, Guid> repo, string table)> NewRepoAsync() {
            var table = $"orders_{Guid.NewGuid():N}";
            await using var connection = new NpgsqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                $"CREATE TABLE {table} (id uuid PRIMARY KEY, description text, total numeric, status text, created_on date)");

            var repo = new PostgresRepository<Order, Guid>(
                _provider.GetRequiredService<IUnitOfWorkFactory>(),
                NullLogger<PostgresRepository<Order, Guid>>.Instance,
                _provider.GetRequiredService<NamingStrategyResolver>(),
                _provider.GetRequiredService<IOptions<DatabaseOptions>>(),
                _provider.GetRequiredService<PostgresBulkInserter>(),
                table);
            return (repo, table);
        }
    }
}
