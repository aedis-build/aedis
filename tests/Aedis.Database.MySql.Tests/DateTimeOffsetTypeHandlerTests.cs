using System.Data;
using Aedis.Database.MySql.TypeHandlers;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Database.MySql.Tests;

/// <summary>
///     <see cref="DateTimeOffset" /> ↔ <c>DATETIME(6)</c>: gravado em UTC e lido de volta com offset zero,
///     inclusive em parâmetro de record posicional contra um MySQL real; a conversão trata os três
///     <see cref="DateTimeKind" /> e rejeita tipos inesperados.
/// </summary>
public sealed class DateTimeOffsetTypeHandlerTests : IClassFixture<MySqlBulkInserterTests.MySqlFixture>
{
    private readonly MySqlBulkInserterTests.MySqlFixture _fixture;

    public DateTimeOffsetTypeHandlerTests(MySqlBulkInserterTests.MySqlFixture fixture) => _fixture = fixture;

    public sealed record Evento(Guid Id, DateTimeOffset CriadoEm, DateTimeOffset? EncerradoEm);

    [Fact]
    public async Task Datetime6_vira_DateTimeOffset_UTC_em_record_posicional() {
        var table = $"eventos_{Guid.NewGuid():N}";
        var criadoEm = new DateTimeOffset(2026, 10, 18, 9, 30, 15, TimeSpan.FromHours(-3)).AddTicks(1234560);
        var aberto = Guid.NewGuid();
        var encerrado = Guid.NewGuid();

        await using (var write = await _fixture.Factory.CreateWriteSessionAsync()) {
            await write.ExecuteAsync($"CREATE TABLE {table} (id CHAR(36) PRIMARY KEY, criado_em DATETIME(6) NOT NULL, encerrado_em DATETIME(6) NULL)");
            await write.ExecuteAsync($"INSERT INTO {table} VALUES (@aberto, @criadoEm, NULL), (@encerrado, @criadoEm, @criadoEm)",
                new { aberto, encerrado, criadoEm });
            await write.CommitAsync();
        }

        await using var read = await _fixture.Factory.CreateReadSessionAsync();
        var eventos = (await read.QueryAsync<Evento>($"SELECT id, criado_em, encerrado_em FROM {table}")).ToDictionary(e => e.Id);

        eventos[aberto].CriadoEm.Should().Be(criadoEm, "o instante é preservado");
        eventos[aberto].CriadoEm.Offset.Should().Be(TimeSpan.Zero, "o MySQL não guarda offset — volta em UTC");
        eventos[aberto].EncerradoEm.Should().BeNull();
        eventos[encerrado].EncerradoEm.Should().Be(criadoEm);
    }

    [Fact]
    public void Parse_trata_Unspecified_como_UTC_converte_Local_e_repassa_DateTimeOffset() {
        var handler = new DateTimeOffsetTypeHandler();
        var utc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var local = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var dto = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(2));

        handler.Parse(utc).Should().Be(new DateTimeOffset(utc, TimeSpan.Zero));
        handler.Parse(local).Should().Be(new DateTimeOffset(local.ToUniversalTime(), TimeSpan.Zero));
        handler.Parse(dto).Should().Be(dto);
        FluentActions.Invoking(() => handler.Parse("2026")).Should().Throw<InvalidCastException>();
    }

    [Fact]
    public void SetValue_grava_UtcDateTime_como_DbType_DateTime() {
        var parameter = Substitute.For<IDbDataParameter>();
        var value = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(-3));

        new DateTimeOffsetTypeHandler().SetValue(parameter, value);

        parameter.DbType.Should().Be(DbType.DateTime);
        parameter.Value.Should().Be(value.UtcDateTime);
    }
}
