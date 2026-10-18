using NSubstitute;
using System.Data;
using Aedis.Database.Postgres.TypeHandlers;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Aedis.Database.Postgres.Tests;

/// <summary>
///     Dicionários <c>string → string</c> viajam como <c>jsonb</c>: o handler serializa/desserializa JSON, marca
///     o parâmetro Npgsql como <c>Jsonb</c>, trata nulo/vazio como nulo e é registrado para os três tipos de
///     dicionário por <c>AddAedisPostgres</c>.
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
    public void Parse_nulo_ou_vazio_vira_nulo() {
        var handler = new JsonDictionaryTypeHandler<IDictionary<string, string?>>();

        handler.Parse(DBNull.Value).Should().BeNull();
        handler.Parse(string.Empty).Should().BeNull();
    }

    [Fact]
    public void Parse_de_tipo_inesperado_lanca() {
        var handler = new JsonDictionaryTypeHandler<Dictionary<string, string?>>();

        var act = () => handler.Parse(42);

        act.Should().Throw<InvalidCastException>();
    }

    [Fact]
    public void SetValue_marca_parametro_Npgsql_como_jsonb() {
        var handler = new JsonDictionaryTypeHandler<Dictionary<string, string?>>();
        var parameter = new NpgsqlParameter();

        handler.SetValue(parameter, new Dictionary<string, string?> { ["k"] = "v" });

        parameter.NpgsqlDbType.Should().Be(NpgsqlDbType.Jsonb);
        parameter.Value.Should().Be("""{"k":"v"}""");
    }

    [Fact]
    public void SetValue_com_nulo_grava_DBNull() {
        var handler = new JsonDictionaryTypeHandler<IReadOnlyDictionary<string, string?>>();
        var parameter = new NpgsqlParameter();

        handler.SetValue(parameter, null);

        parameter.Value.Should().Be(DBNull.Value);
    }

    [Fact]
    public void SetValue_em_parametro_generico_usa_DbType_String() {
        var handler = new JsonDictionaryTypeHandler<Dictionary<string, string?>>();
        var parameter = new DbParameterStub();

        handler.SetValue(parameter, new Dictionary<string, string?> { ["k"] = "v" });

        parameter.DbType.Should().Be(DbType.String);
    }

    [Fact]
    public void AddAedisPostgres_registra_os_tres_tipos_de_dicionario() {
        new ServiceCollection().AddLogging().AddAedisPostgres(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "Host=x" }).Build());

        SqlMapper.HasTypeHandler(typeof(Dictionary<string, string?>)).Should().BeTrue();
        SqlMapper.HasTypeHandler(typeof(IDictionary<string, string?>)).Should().BeTrue();
        SqlMapper.HasTypeHandler(typeof(IReadOnlyDictionary<string, string?>)).Should().BeTrue();
    }

    private sealed class DbParameterStub : IDbDataParameter
    {
        public DbType DbType { get; set; }
        public ParameterDirection Direction { get; set; }
        public bool IsNullable => true;
        [System.Diagnostics.CodeAnalysis.AllowNull] public string ParameterName { get; set; } = "";
        [System.Diagnostics.CodeAnalysis.AllowNull] public string SourceColumn { get; set; } = "";
        public DataRowVersion SourceVersion { get; set; }
        public object? Value { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public int Size { get; set; }
    }

    [Fact]
    public void Parse_rejeita_tipos_inesperados_e_SetValue_cai_em_string_fora_do_Npgsql() {
        var handler = new JsonDictionaryTypeHandler<Dictionary<string, string?>>();
        FluentActions.Invoking(() => handler.Parse(123)).Should().Throw<InvalidCastException>();
        FluentActions.Invoking(() => new JsonDictionaryTypeHandler<SortedDictionary<string, string?>>().Parse("{}"))
            .Should().Throw<InvalidCastException>();
        handler.Parse(DBNull.Value).Should().BeNull();

        var generic = Substitute.For<IDbDataParameter>();
        handler.SetValue(generic, null);
        generic.DbType.Should().Be(DbType.String);
        generic.Value.Should().Be(DBNull.Value);

        JsonDictionaryTypeHandler<Dictionary<string, string?>>.Serialize([new KeyValuePair<string, string?>("k", "v")])
            .Should().Be("""{"k":"v"}""");
    }
}
