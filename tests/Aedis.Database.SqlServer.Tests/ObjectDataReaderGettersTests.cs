using System.Data;
using System.Reflection;
using Aedis.Database.SqlServer.Internal;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>Getters tipados, metadados e membros não suportados do leitor usado pelo SqlBulkCopy.</summary>
public sealed class ObjectDataReaderGettersTests
{
    private sealed class Typed
    {
        public bool B { get; init; } = true;
        public byte By { get; init; } = 7;
        public char C { get; init; } = 'x';
        public DateTime Dt { get; init; } = new(2026, 1, 2);
        public decimal De { get; init; } = 1.5m;
        public double Do { get; init; } = 2.5;
        public float F { get; init; } = 3.5f;
        public Guid G { get; init; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public short S { get; init; } = 4;
        public int I { get; init; } = 5;
        public long L { get; init; } = 6;
        public string Str { get; init; } = "s";
    }

    private static readonly PropertyInfo[] Props = typeof(Typed).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void Getters_tipados_indexadores_e_metadados() {
        using var reader = new ObjectDataReader<Typed>(Props, [new Typed()]);
        reader.Read().Should().BeTrue();

        reader.GetBoolean(reader.GetOrdinal("B")).Should().BeTrue();
        reader.GetByte(reader.GetOrdinal("By")).Should().Be(7);
        reader.GetChar(reader.GetOrdinal("C")).Should().Be('x');
        reader.GetDateTime(reader.GetOrdinal("Dt")).Should().Be(new DateTime(2026, 1, 2));
        reader.GetDecimal(reader.GetOrdinal("De")).Should().Be(1.5m);
        reader.GetDouble(reader.GetOrdinal("Do")).Should().Be(2.5);
        reader.GetFloat(reader.GetOrdinal("F")).Should().Be(3.5f);
        reader.GetGuid(reader.GetOrdinal("G")).Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        reader.GetInt16(reader.GetOrdinal("S")).Should().Be(4);
        reader.GetInt32(reader.GetOrdinal("I")).Should().Be(5);
        reader.GetInt64(reader.GetOrdinal("L")).Should().Be(6);
        reader.GetString(reader.GetOrdinal("Str")).Should().Be("s");

        reader.GetName(0).Should().Be("B");
        reader.GetDataTypeName(reader.GetOrdinal("I")).Should().Be("Int32");
        reader[0].Should().Be(true);
        reader["Str"].Should().Be("s");
        var values = new object[3];
        reader.GetValues(values).Should().Be(3);
        values[0].Should().Be(true);

        reader.Depth.Should().Be(0);
        reader.RecordsAffected.Should().Be(-1);
        reader.NextResult().Should().BeFalse();
        reader.GetSchemaTable().Should().BeNull();
        reader.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void Nome_desconhecido_e_membros_nao_suportados_lancam() {
        using var reader = new ObjectDataReader<Typed>(Props, [new Typed()]);
        reader.Read();

        var ordinal = () => reader.GetOrdinal("nope");
        var bytes = () => reader.GetBytes(0, 0, null, 0, 0);
        var chars = () => reader.GetChars(0, 0, null, 0, 0);
        var data = () => reader.GetData(0);

        ordinal.Should().Throw<IndexOutOfRangeException>();
        bytes.Should().Throw<NotSupportedException>();
        chars.Should().Throw<NotSupportedException>();
        data.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Close_marca_fechado_e_dispose_e_idempotente() {
        var reader = new ObjectDataReader<Typed>(Props, [new Typed()]);

        reader.Close();
        reader.Dispose();
        reader.Dispose();

        reader.IsClosed.Should().BeTrue();
    }
}
