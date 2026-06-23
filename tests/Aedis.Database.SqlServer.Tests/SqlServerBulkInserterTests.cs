using System.Reflection;
using Aedis.Database.Abstractions;
using Aedis.Database.SqlServer;
using Aedis.Database.SqlServer.Naming;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>
///     Bulk insert/upsert via SqlBulkCopy em streaming contra um SQL Server real (Testcontainers). Prova o
///     caminho de alta performance, em paridade com o COPY do PostgreSQL: SqlBulkCopy direto de muitas
///     linhas e o upsert chunked (staging temporária reaproveitada + MERGE), tratando enums (string
///     maiúscula), <see cref="DateOnly" /> e nulos. Integração opt-in (a imagem do SQL Server é pesada):
///     ligue com a env <c>AEDIS_SQLSERVER_IT=1</c>.
/// </summary>
public sealed class SqlServerBulkInserterTests : IClassFixture<SqlServerBulkInserterTests.SqlServerFixture>
{
    private static readonly PropertyInfo[] Props =
        typeof(BulkItem).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private readonly SqlServerFixture _fixture;

    public SqlServerBulkInserterTests(SqlServerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task BulkInsert_via_sqlbulkcopy_insere_muitas_linhas() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
        const int total = 20_000;
        var table = await _fixture.CreateTableAsync();
        var items = Enumerable.Range(0, total).Select(BulkItem.Create).ToList();

        await using (var uow = await _fixture.Factory.CreateWriteSessionAsync()) {
            await _fixture.Inserter.BulkInsertAsync(uow, table, Props, items, _fixture.Naming, _fixture.Options);
            await uow.CommitAsync();
        }

        (await _fixture.CountAsync(table)).Should().Be(total);
    }

    [SkippableFact]
    public async Task BulkInsertChunked_com_chave_faz_upsert_via_merge() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_SQLSERVER_IT=1 para rodar a integração SQL Server.");
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
        (await _fixture.ScalarAsync<int>($"SELECT count(*) FROM {table} WHERE name = 'v2'")).Should().Be(5_000);
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

    public sealed class SqlServerFixture : IAsyncLifetime
    {
        private MsSqlContainer? _container;
        private ServiceProvider? _provider;

        /// <summary>Integração opt-in (a imagem do SQL Server é pesada): liga com a env <c>AEDIS_SQLSERVER_IT=1</c>.</summary>
        public bool Enabled { get; } = Environment.GetEnvironmentVariable("AEDIS_SQLSERVER_IT") == "1";

        public IUnitOfWorkFactory Factory => _provider!.GetRequiredService<IUnitOfWorkFactory>();
        public SqlServerBulkInserter Inserter => _provider!.GetRequiredService<SqlServerBulkInserter>();
        public NamingStrategyResolver Naming => _provider!.GetRequiredService<NamingStrategyResolver>();
        public DatabaseOptions Options => _provider!.GetRequiredService<IOptions<DatabaseOptions>>().Value;

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

        public async Task<string> CreateTableAsync() {
            var table = $"bulk_items_{Guid.NewGuid():N}";
            await using var connection = new SqlConnection(_container!.GetConnectionString());
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                $"CREATE TABLE {table} (id UNIQUEIDENTIFIER PRIMARY KEY, name NVARCHAR(200), amount DECIMAL(18,4), "
                + "status NVARCHAR(50), created_on DATE)");
            return table;
        }

        public async Task<int> CountAsync(string table) => await ScalarAsync<int>($"SELECT count(*) FROM {table}");

        public async Task<T> ScalarAsync<T>(string sql) {
            await using var connection = new SqlConnection(_container!.GetConnectionString());
            await connection.OpenAsync();
            return (await connection.ExecuteScalarAsync<T>(sql))!;
        }
    }
}
