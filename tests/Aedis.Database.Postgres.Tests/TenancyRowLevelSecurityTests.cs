using Aedis.Database.Abstractions;
using Aedis.Tenancy;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Aedis.Database.Postgres.Tests;

/// <summary>
///     Multi-tenancy contra um PostgreSQL real: a <see cref="UnitOfWorkFactory" /> grava o tenant do escopo em
///     <c>aedis.tenant_id</c> e a política de Row-Level Security do <c>docs/TENANCY.md</c> isola os tenants.
///     Prova que ler ou gravar dado de outro tenant é impossível mesmo com SQL que tenta (filtro explícito,
///     <c>INSERT</c> com tenant alheio), que sem tenant a consulta falha fechado e que a variável não vaza pelo
///     pool de conexões nem entre fluxos concorrentes.
/// </summary>
public sealed class TenancyRowLevelSecurityTests : IClassFixture<TenancyRowLevelSecurityTests.RlsFixture>
{
    private readonly RlsFixture _fixture;

    public TenancyRowLevelSecurityTests(RlsFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Tenant_so_le_as_proprias_linhas_mesmo_pedindo_as_de_outro() {
        using (_fixture.Tenant.BeginScope("acme")) {
            await using var session = await _fixture.Factory.CreateReadSessionAsync();

            (await session.QueryAsync<string>("SELECT title FROM docs ORDER BY title"))
                .Should().Equal("acme-1", "acme-2");
            (await session.QueryAsync<string>("SELECT title FROM docs WHERE tenant_id = 'globex'"))
                .Should().BeEmpty("a política esconde as linhas do outro tenant");
        }
    }

    [Fact]
    public async Task Tenant_nao_grava_linha_em_nome_de_outro() {
        using (_fixture.Tenant.BeginScope("acme")) {
            await using var session = await _fixture.Factory.CreateWriteSessionAsync();

            var act = () => session.ExecuteAsync(
                "INSERT INTO docs (id, tenant_id, title) VALUES (@id, 'globex', 'intrusa')", new { id = Guid.NewGuid() });

            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        }
    }

    [Fact]
    public async Task Tenant_nao_altera_nem_apaga_linha_de_outro() {
        using (_fixture.Tenant.BeginScope("acme")) {
            await using var session = await _fixture.Factory.CreateWriteSessionAsync();

            (await session.ExecuteAsync("UPDATE docs SET title = 'x' WHERE tenant_id = 'globex'")).Should().Be(0);
            (await session.ExecuteAsync("DELETE FROM docs WHERE tenant_id = 'globex'")).Should().Be(0);
            await session.RollbackAsync();
        }

        (await _fixture.CountAsSuperuserAsync("globex")).Should().Be(1);
    }

    [Fact]
    public async Task Sem_tenant_a_consulta_falha_fechado() {
        await using var session = await _fixture.Factory.CreateReadSessionAsync();

        var act = () => session.QueryAsync<string>("SELECT title FROM docs");

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task Tenant_nao_vaza_para_a_proxima_sessao_do_pool() {
        using (_fixture.Tenant.BeginScope("acme")) {
            await using var session = await _fixture.Factory.CreateReadSessionAsync();
            await session.QueryAsync<string>("SELECT title FROM docs");
        }

        for (var i = 0; i < 5; i++) {
            await using var session = await _fixture.Factory.CreateReadSessionAsync();
            var act = () => session.QueryAsync<string>("SELECT title FROM docs");
            await act.Should().ThrowAsync<PostgresException>();
        }
    }

    [Fact]
    public async Task Fluxos_concorrentes_de_tenants_diferentes_ficam_isolados() {
        async Task<string[]> ReadAs(string tenant) {
            using (_fixture.Tenant.BeginScope(tenant)) {
                await using var session = await _fixture.Factory.CreateReadSessionAsync();
                return (await session.QueryAsync<string>("SELECT DISTINCT tenant_id FROM docs")).ToArray();
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => ReadAs(i % 2 == 0 ? "acme" : "globex")));

        for (var i = 0; i < results.Length; i++)
            results[i].Should().Equal(i % 2 == 0 ? "acme" : "globex");
    }

    /// <summary>
    ///     Sobe o PostgreSQL, aplica o SQL do <c>docs/TENANCY.md</c> como superusuário e conecta a fábrica com um
    ///     papel de aplicação sem <c>BYPASSRLS</c> — superusuário e dono da tabela ignorariam a política.
    /// </summary>
    public sealed class RlsFixture : IAsyncLifetime
    {
        private const string AppPassword = "app-test-only";

        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        public AmbientTenantContext Tenant { get; } = new();

        public IUnitOfWorkFactory Factory { get; private set; } = null!;

        public Task DisposeAsync() => _container.DisposeAsync().AsTask();

        public async Task InitializeAsync() {
            await _container.StartAsync();
            await using (var connection = new NpgsqlConnection(_container.GetConnectionString())) {
                await connection.OpenAsync();
                await connection.ExecuteAsync($"""
                    CREATE ROLE app_user LOGIN PASSWORD '{AppPassword}';
                    CREATE OR REPLACE FUNCTION aedis_current_tenant() RETURNS text
                    LANGUAGE plpgsql STABLE AS $$
                    DECLARE tenant text := nullif(current_setting('aedis.tenant_id', true), '');
                    BEGIN
                        IF tenant IS NULL THEN
                            RAISE EXCEPTION 'aedis: acesso a dado escopado sem tenant' USING ERRCODE = '42501';
                        END IF;
                        RETURN tenant;
                    END $$;
                    CREATE TABLE docs (id uuid PRIMARY KEY, tenant_id text NOT NULL, title text NOT NULL);
                    ALTER TABLE docs ENABLE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON docs
                        USING (tenant_id = (SELECT aedis_current_tenant()))
                        WITH CHECK (tenant_id = (SELECT aedis_current_tenant()));
                    GRANT SELECT, INSERT, UPDATE, DELETE ON docs TO app_user;
                    INSERT INTO docs VALUES
                        (gen_random_uuid(), 'acme', 'acme-1'),
                        (gen_random_uuid(), 'acme', 'acme-2'),
                        (gen_random_uuid(), 'globex', 'globex-1');
                    """);
            }

            var appConnection = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) {
                Username = "app_user", Password = AppPassword
            }.ConnectionString;
            Factory = new UnitOfWorkFactory(Options.Create(new DatabaseOptions { ConnectionString = appConnection }),
                NullLogger<UnitOfWork>.Instance, Tenant);
        }

        public async Task<long> CountAsSuperuserAsync(string tenant) {
            await using var connection = new NpgsqlConnection(_container.GetConnectionString());
            return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM docs WHERE tenant_id = @tenant",
                new { tenant });
        }
    }
}
