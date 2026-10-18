using FluentAssertions;
using Xunit;

namespace Aedis.Database.Postgres.Tests;

/// <summary>
///     Coluna <c>timestamptz</c> materializada em parâmetro <see cref="DateTimeOffset" /> de record posicional,
///     contra um PostgreSQL real: sem o handler, o Dapper lança "no matching constructor" (vê <see cref="DateTime" />).
/// </summary>
public sealed class DateTimeOffsetTypeHandlerTests : IClassFixture<PostgresBulkInserterTests.PostgresFixture>
{
    private readonly PostgresBulkInserterTests.PostgresFixture _fixture;

    public DateTimeOffsetTypeHandlerTests(PostgresBulkInserterTests.PostgresFixture fixture) => _fixture = fixture;

    public sealed record Evento(Guid Id, DateTimeOffset CriadoEm, DateTimeOffset? EncerradoEm);

    [Fact]
    public async Task Timestamptz_vira_DateTimeOffset_em_record_posicional() {
        var table = $"eventos_{Guid.NewGuid():N}";
        var criadoEm = new DateTimeOffset(2026, 10, 18, 12, 30, 15, TimeSpan.Zero);
        var aberto = Guid.NewGuid();
        var encerrado = Guid.NewGuid();

        await using (var write = await _fixture.Factory.CreateWriteSessionAsync()) {
            await write.ExecuteAsync($"CREATE TABLE {table} (id uuid PRIMARY KEY, criado_em timestamptz NOT NULL, encerrado_em timestamptz)");
            await write.ExecuteAsync($"INSERT INTO {table} VALUES (@aberto, @criadoEm, NULL), (@encerrado, @criadoEm, @criadoEm)",
                new { aberto, encerrado, criadoEm });
            await write.CommitAsync();
        }

        await using var read = await _fixture.Factory.CreateReadSessionAsync();
        var eventos = (await read.QueryAsync<Evento>($"SELECT id, criado_em, encerrado_em FROM {table}")).ToDictionary(e => e.Id);

        eventos[aberto].CriadoEm.Should().Be(criadoEm);
        eventos[aberto].EncerradoEm.Should().BeNull();
        eventos[encerrado].EncerradoEm.Should().Be(criadoEm);
    }
}
