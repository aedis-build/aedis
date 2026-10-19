using System.Reflection;
using Aedis.Database.Abstractions;
using Aedis.Database.Naming;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using NSubstitute;
using Testcontainers.MySql;
using Xunit;

namespace Aedis.Database.MySql.Tests;

/// <summary>
///     Bulk insert/upsert via INSERT multi-linha em lotes contra um MySQL real (Testcontainers): carga direta
///     de muitas linhas, upsert chunked sem guarda (<c>ON DUPLICATE KEY UPDATE</c> direto no INSERT),
///     tratando enums (nome declarado), <see cref="DateOnly" /> e nulos; lista vazia não toca o banco.
/// </summary>
public sealed class MySqlBulkInserterTests : IClassFixture<MySqlBulkInserterTests.MySqlFixture>
{
    private static readonly PropertyInfo[] Props =
        typeof(BulkItem).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private readonly MySqlFixture _fixture;

    public MySqlBulkInserterTests(MySqlFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task BulkInsert_multi_linha_insere_muitas_linhas_em_lotes() {
        const int total = 20_000;
        var table = await _fixture.CreateTableAsync();
        var items = Enumerable.Range(0, total).Select(BulkItem.Create).ToList();

        await using (var uow = await _fixture.Factory.CreateWriteSessionAsync()) {
            await _fixture.Inserter.BulkInsertAsync(uow, table, Props, items, _fixture.Naming, _fixture.Options);
            await uow.CommitAsync();
        }

        (await _fixture.CountAsync(table)).Should().Be(total);
        (await _fixture.ScalarAsync<string>($"SELECT status FROM {table} WHERE name = 'item-1'")).Should().Be("Settled");
    }

    [Fact]
    public async Task BulkInsertChunked_com_spec_sem_guarda_faz_upsert_no_proprio_insert() {
        var table = await _fixture.CreateTableAsync();
        var ids = Enumerable.Range(0, 5_000).Select(_ => Guid.NewGuid()).ToList();
        var upsert = UpsertSpec.OnKey("Id");

        var first = ids.Select((id, i) => BulkItem.WithId(id, i, "v1")).ToList();
        await using (var uow = await _fixture.Factory.CreateWriteSessionAsync()) {
            await _fixture.Inserter.BulkInsertChunkedAsync(uow, table, Props, first, _fixture.Naming,
                _fixture.Options, 1_000, upsert);
            await uow.CommitAsync();
        }

        var second = ids.Select((id, i) => BulkItem.WithId(id, i, "v2")).ToList();
        await using (var uow = await _fixture.Factory.CreateWriteSessionAsync()) {
            await _fixture.Inserter.BulkInsertChunkedAsync(uow, table, Props, second, _fixture.Naming,
                _fixture.Options, 1_000, upsert);
            await uow.CommitAsync();
        }

        (await _fixture.CountAsync(table)).Should().Be(5_000, "o upsert atualiza, não duplica");
        (await _fixture.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE name = 'v2'")).Should().Be(5_000);
    }

    [Fact]
    public async Task Lista_vazia_nao_toca_o_banco() {
        var uow = Substitute.For<IUnitOfWork>();

        await _fixture.Inserter.BulkInsertAsync(uow, "x", Props, Array.Empty<BulkItem>(), _fixture.Naming, _fixture.Options);

        await uow.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
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

    public sealed class MySqlFixture : IAsyncLifetime
    {
        private readonly MySqlContainer _container = new MySqlBuilder()
            .WithImage("mysql:8.4")
            .Build();

        private ServiceProvider _provider = null!;

        public IUnitOfWorkFactory Factory { get; private set; } = null!;
        public MySqlBulkInserter Inserter { get; private set; } = null!;
        public NamingStrategyResolver Naming { get; private set; } = null!;
        public DatabaseOptions Options { get; private set; } = null!;

        public async Task InitializeAsync() {
            await _container.StartAsync();

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Database:ConnectionString"] = _container.GetConnectionString()
            }).Build();

            _provider = new ServiceCollection().AddLogging().AddAedisMySql(config).BuildServiceProvider();
            Factory = _provider.GetRequiredService<IUnitOfWorkFactory>();
            Inserter = _provider.GetRequiredService<MySqlBulkInserter>();
            Naming = _provider.GetRequiredService<NamingStrategyResolver>();
            Options = _provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        }

        public async Task DisposeAsync() {
            await _provider.DisposeAsync();
            await _container.DisposeAsync();
        }

        public async Task<string> CreateTableAsync() {
            var table = $"bulk_items_{Guid.NewGuid():N}";
            await using var connection = new MySqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                $"CREATE TABLE {table} (id CHAR(36) PRIMARY KEY, name VARCHAR(100), amount DECIMAL(18,2), status VARCHAR(32), created_on DATE)");
            return table;
        }

        public async Task<long> CountAsync(string table) => await ScalarAsync<long>($"SELECT count(*) FROM {table}");

        public async Task<T> ScalarAsync<T>(string sql) {
            await using var connection = new MySqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();
            return (await connection.ExecuteScalarAsync<T>(sql))!;
        }
    }
}
