using System.Data;
using Aedis.Database.MySql.TypeHandlers;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Aedis.Database.MySql.Tests;

/// <summary>
///     Dicionários <c>string → string</c> viajam como texto JSON para a coluna <c>JSON</c>: o handler
///     serializa/desserializa, trata nulo/vazio como nulo e é registrado para os três tipos de dicionário
///     por <c>AddAedisMySql</c>.
/// </summary>
public sealed class JsonDictionaryTypeHandlerTests
{
    [Fact]
    public void Parse_le_json_em_dicionario() {
        var handler = new JsonDictionaryTypeHandler<Dictionary<string, string?>>();

        var result = handler.Parse("""{"a":"1","b":null}""");

        result.Should().NotBeNull();
        result!.Should().Contain("a", "1");
        result.Should().ContainKey("b").WhoseValue.Should().BeNull();
    }

    [Fact]
    public void Parse_nulo_ou_vazio_vira_nulo_e_tipos_inesperados_lancam() {
        var handler = new JsonDictionaryTypeHandler<IDictionary<string, string?>>();

        handler.Parse(DBNull.Value).Should().BeNull();
        handler.Parse(string.Empty).Should().BeNull();
        FluentActions.Invoking(() => handler.Parse(42)).Should().Throw<InvalidCastException>();
        FluentActions.Invoking(() => new JsonDictionaryTypeHandler<SortedDictionary<string, string?>>().Parse("{}"))
            .Should().Throw<InvalidCastException>();
    }

    [Fact]
    public void SetValue_grava_texto_JSON_ou_DBNull() {
        var handler = new JsonDictionaryTypeHandler<IReadOnlyDictionary<string, string?>>();
        var parameter = Substitute.For<IDbDataParameter>();

        handler.SetValue(parameter, new Dictionary<string, string?> { ["k"] = "v" });
        parameter.DbType.Should().Be(DbType.String);
        parameter.Value.Should().Be("""{"k":"v"}""");

        handler.SetValue(parameter, null);
        parameter.Value.Should().Be(DBNull.Value);

        JsonDictionaryTypeHandler<Dictionary<string, string?>>.Serialize([new KeyValuePair<string, string?>("k", "v")])
            .Should().Be("""{"k":"v"}""");
    }

    [Fact]
    public void AddAedisMySql_registra_os_tres_tipos_de_dicionario() {
        new ServiceCollection().AddLogging().AddAedisMySql(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "Server=x" }).Build());

        SqlMapper.HasTypeHandler(typeof(Dictionary<string, string?>)).Should().BeTrue();
        SqlMapper.HasTypeHandler(typeof(IDictionary<string, string?>)).Should().BeTrue();
        SqlMapper.HasTypeHandler(typeof(IReadOnlyDictionary<string, string?>)).Should().BeTrue();
    }
}
