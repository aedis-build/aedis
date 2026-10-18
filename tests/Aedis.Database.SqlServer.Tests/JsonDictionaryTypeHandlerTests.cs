using System.Data;
using System.Reflection;
using Aedis.Database.SqlServer.Internal;
using Aedis.Database.SqlServer.TypeHandlers;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>
///     Dicionários <c>string → string</c> viajam como JSON em <c>nvarchar(max)</c>: o handler serializa e
///     desserializa, o <see cref="ObjectDataReader{T}" /> do SqlBulkCopy projeta a coluna como string JSON, e
///     <c>AddAedisSqlServer</c> registra o handler para os três tipos de dicionário.
/// </summary>
public sealed class JsonDictionaryTypeHandlerTests
{
    private sealed class Item
    {
        public Guid Id { get; init; }
        public Dictionary<string, string?>? Metadata { get; init; }
    }

    [Fact]
    public void Parse_le_json_e_trata_nulo_e_vazio() {
        var handler = new JsonDictionaryTypeHandler<Dictionary<string, string?>>();

        handler.Parse("""{"a":"1"}""")!.Should().Contain("a", "1");
        handler.Parse(DBNull.Value).Should().BeNull();
        handler.Parse(string.Empty).Should().BeNull();
    }

    [Fact]
    public void SetValue_grava_JSON_como_string() {
        var handler = new JsonDictionaryTypeHandler<IDictionary<string, string?>>();
        var parameter = new SqlParameter();

        handler.SetValue(parameter, new Dictionary<string, string?> { ["k"] = "v" });

        parameter.DbType.Should().Be(DbType.String);
        parameter.Value.Should().Be("""{"k":"v"}""");
    }

    [Fact]
    public void ObjectDataReader_projeta_dicionario_como_JSON() {
        var props = typeof(Item).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var items = new[] {
            new Item { Id = Guid.NewGuid(), Metadata = new Dictionary<string, string?> { ["k"] = "v" } },
            new Item { Id = Guid.NewGuid(), Metadata = null }
        };
        using var reader = new ObjectDataReader<Item>(props, items);
        var ordinal = reader.GetOrdinal(nameof(Item.Metadata));

        reader.GetFieldType(ordinal).Should().Be(typeof(string));
        reader.Read().Should().BeTrue();
        reader.GetValue(ordinal).Should().Be("""{"k":"v"}""");
        reader.Read().Should().BeTrue();
        reader.GetValue(ordinal).Should().Be(DBNull.Value);
    }

    [Fact]
    public void AddAedisSqlServer_registra_os_tres_tipos_de_dicionario() {
        new ServiceCollection().AddLogging().AddAedisSqlServer(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "Server=x" }).Build());

        SqlMapper.HasTypeHandler(typeof(Dictionary<string, string?>)).Should().BeTrue();
        SqlMapper.HasTypeHandler(typeof(IDictionary<string, string?>)).Should().BeTrue();
        SqlMapper.HasTypeHandler(typeof(IReadOnlyDictionary<string, string?>)).Should().BeTrue();
    }

    [Fact]
    public void Parse_rejeita_tipos_inesperados_e_json_incompativel_e_SetValue_grava_NULL_e_enumeraveis() {
        var handler = new JsonDictionaryTypeHandler<Dictionary<string, string?>>();
        FluentActions.Invoking(() => handler.Parse(123)).Should().Throw<InvalidCastException>();
        FluentActions.Invoking(() => new JsonDictionaryTypeHandler<SortedDictionary<string, string?>>().Parse("{}"))
            .Should().Throw<InvalidCastException>();

        var parameter = new SqlParameter();
        handler.SetValue(parameter, null);
        parameter.Value.Should().Be(DBNull.Value);

        JsonDictionaryTypeHandler<Dictionary<string, string?>>.Serialize([new KeyValuePair<string, string?>("k", "v")])
            .Should().Be("""{"k":"v"}""");
    }
}
