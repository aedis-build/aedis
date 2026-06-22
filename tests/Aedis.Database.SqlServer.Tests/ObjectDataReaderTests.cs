using System.Data;
using System.Reflection;
using Aedis.Database.SqlServer.Internal;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>
///     <see cref="ObjectDataReader{T}" /> (o <see cref="IDataReader" /> em streaming que alimenta o
///     SqlBulkCopy): projeta as linhas sob demanda e aplica as mesmas conversões do write path — enum vira
///     string maiúscula, <see cref="DateOnly" />/<see cref="TimeOnly" /> viram tipos do SQL Server e nulos
///     viram <see cref="DBNull" />.
/// </summary>
public sealed class ObjectDataReaderTests
{
    private static readonly PropertyInfo[] Props =
        typeof(Item).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private enum Kind { Pending, Settled }

    [Fact]
    public void Enumera_linhas_em_streaming_e_expoe_field_count() {
        var items = new[] { Item.New(1), Item.New(2), Item.New(3) };
        using var reader = new ObjectDataReader<Item>(Props, items);

        reader.FieldCount.Should().Be(Props.Length);

        var rows = 0;
        while (reader.Read())
            rows++;
        rows.Should().Be(3);
    }

    [Fact]
    public void Converte_enum_dateonly_timeonly_e_nulos() {
        var item = new Item {
            Id = Guid.NewGuid(), Name = null, Kind = Kind.Settled,
            Day = new DateOnly(2026, 1, 15), At = new TimeOnly(13, 30)
        };
        using var reader = new ObjectDataReader<Item>(Props, new[] { item });
        reader.Read().Should().BeTrue();

        Value(reader, "Kind").Should().Be("SETTLED");
        Value(reader, "Name").Should().Be(DBNull.Value);
        Value(reader, "Day").Should().Be(new DateTime(2026, 1, 15));
        Value(reader, "At").Should().Be(new TimeSpan(13, 30, 0));
        reader.IsDBNull(reader.GetOrdinal("Name")).Should().BeTrue();
    }

    [Fact]
    public void Field_type_reflete_o_tipo_de_envio() {
        using var reader = new ObjectDataReader<Item>(Props, Array.Empty<Item>());

        reader.GetFieldType(reader.GetOrdinal("Kind")).Should().Be(typeof(string));
        reader.GetFieldType(reader.GetOrdinal("Day")).Should().Be(typeof(DateTime));
        reader.GetFieldType(reader.GetOrdinal("At")).Should().Be(typeof(TimeSpan));
        reader.GetFieldType(reader.GetOrdinal("Id")).Should().Be(typeof(Guid));
    }

    private static object Value(ObjectDataReader<Item> reader, string property) =>
        reader.GetValue(reader.GetOrdinal(property));

    private sealed class Item
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public Kind Kind { get; set; }
        public DateOnly Day { get; set; }
        public TimeOnly At { get; set; }

        public static Item New(int seed) => new() {
            Id = Guid.NewGuid(), Name = $"item-{seed}", Kind = Kind.Pending,
            Day = new DateOnly(2026, 1, 1), At = new TimeOnly(8, 0)
        };
    }
}
