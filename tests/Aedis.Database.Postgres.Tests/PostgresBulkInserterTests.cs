using System.Reflection;
using Aedis.Database.Abstractions;
using Aedis.Database.Postgres;
using Aedis.Database.Postgres.Naming;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Aedis.Database.Postgres.Tests;

/// <summary>
///     Bulk insert/upsert via COPY binário contra um PostgreSQL real (Testcontainers). Prova o caminho de
///     alta performance: COPY direto de muitas linhas e o upsert chunked (temp table + ON CONFLICT),
///     tratando enums (string maiúscula), <see cref="DateOnly" /> e nulos.
/// </summary>
public sealed class PostgresBulkInserterTests : IClassFixture<PostgresBulkInserterTests.PostgresFixture>
{
    private static readonly PropertyInfo[] Props =
        typeof(BulkItem).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private readonly PostgresFixture _fixture;

    public PostgresBulkInserterTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task BulkInsert_via_copy_insere_muitas_linhas() {
        const int total = 20_000;
        var table = await _fixture.CreateTableAsync();
        var items = Enumerable.Range(0, total).Select(BulkItem.Create).ToList();

        await using (var uow = await _fixture.Factory.CreateWriteSessionAsync()) {
            await _fixture.Inserter.BulkInsertAsync(uow, table, Props, items, _fixture.Naming, _fixture.Options);
            await uow.CommitAsync();
        }

        (await _fixture.CountAsync(table)).Should().Be(total);
    }

    [Fact]
    public async Task BulkInsertChunked_com_on_conflict_faz_upsert() {
        var table = await _fixture.CreateTableAsync();
        var ids = Enumerable.Range(0, 5_000).Select(_ => Guid.NewGuid()).ToList();
        var onConflict = "ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, amount = EXCLUDED.amount";

        var first = ids.Select((id, i) => BulkItem.WithId(id, i, "v1")).ToList();
        await using (var uow = await _fixture.Factory.CreateWriteSessionAsync()) {
            await _fixture.Inserter.BulkInsertChunkedAsync(uow, table, Props, first, _fixture.Naming,
                _fixture.Options, 1_000, onConflict);
            await uow.CommitAsync();
        }

        var second = ids.Select((id, i) => BulkItem.WithId(id, i, "v2")).ToList();
        await using (var uow = await _fixture.Factory.CreateWriteSessionAsync()) {
            await _fixture.Inserter.BulkInsertChunkedAsync(uow, table, Props, second, _fixture.Naming,
                _fixture.Options, 1_000, onConflict);
            await uow.CommitAsync();
        }

        (await _fixture.CountAsync(table)).Should().Be(5_000, "o upsert atualiza, não duplica");
        (await _fixture.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE name = 'v2'")).Should().Be(5_000);
    }

    public enum ItemStatus { Pending, Settled }

    public sealed class BulkItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public ItemStatus Status { get; set; }
        public DateOnly CreatedOn { get; set; }

        public static BulkItem Create(int i) => WithId(Guid.NewGuid(), i, $"item-{i}");

        public static BulkItem WithId(Guid id, int i, string name) => new() {
            Id = id,
            Name = name,
            Amount = i * 1.5m,
            Status = i % 2 == 0 ? ItemStatus.Pending : ItemStatus.Settled,
            CreatedOn = new DateOnly(2026, 1, 1).AddDays(i % 28)
        };
    }

    public sealed class PostgresFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        private ServiceProvider _provider = null!;

        public IUnitOfWorkFactory Factory { get; private set; } = null!;
        public PostgresBulkInserter Inserter { get; private set; } = null!;
        public NamingStrategyResolver Naming { get; private set; } = null!;
        public DatabaseOptions Options { get; private set; } = null!;

        public async Task InitializeAsync() {
            await _container.StartAsync();

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Database:ConnectionString"] = _container.GetConnectionString()
            }).Build();

            _provider = new ServiceCollection().AddLogging().AddAedisPostgres(config).BuildServiceProvider();
            Factory = _provider.GetRequiredService<IUnitOfWorkFactory>();
            Inserter = _provider.GetRequiredService<PostgresBulkInserter>();
            Naming = _provider.GetRequiredService<NamingStrategyResolver>();
            Options = _provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        }

        public async Task DisposeAsync() {
            await _provider.DisposeAsync();
            await _container.DisposeAsync();
        }

        public async Task<string> CreateTableAsync() {
            var table = $"bulk_items_{Guid.NewGuid():N}";
            await using var connection = new NpgsqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                $"CREATE TABLE {table} (id uuid PRIMARY KEY, name text, amount numeric, status text, created_on date)");
            return table;
        }

        public async Task<long> CountAsync(string table) => await ScalarAsync<long>($"SELECT count(*) FROM {table}");

        public async Task<T> ScalarAsync<T>(string sql) {
            await using var connection = new NpgsqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            return (await connection.ExecuteScalarAsync<T>(sql))!;
        }
    }
}
