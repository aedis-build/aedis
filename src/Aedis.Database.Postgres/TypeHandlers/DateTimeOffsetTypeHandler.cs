using System.Data;
using Dapper;

namespace Aedis.Database.Postgres.TypeHandlers;

/// <summary>
///     Dapper TypeHandler para <see cref="DateTimeOffset" /> ↔ coluna <c>timestamptz</c>. O Npgsql devolve
///     <c>timestamptz</c> como <see cref="DateTime" /> UTC; sem handler, o Dapper não casa a coluna com um parâmetro
///     <see cref="DateTimeOffset" /> de construtor (record posicional) e lança "no matching constructor".
/// </summary>
public sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    /// <summary>Converte o valor lido (<see cref="DateTime" /> ou <see cref="DateTimeOffset" />) em <see cref="DateTimeOffset" />.</summary>
    public override DateTimeOffset Parse(object value) => value switch {
        DateTimeOffset dto => dto,
        // timestamp sem fuso chega Unspecified: tratado como UTC, não como hora local da máquina.
        DateTime { Kind: DateTimeKind.Unspecified } dt => new DateTimeOffset(dt, TimeSpan.Zero),
        DateTime dt => new DateTimeOffset(dt),
        _ => throw new InvalidCastException($"Não é possível converter {value.GetType()} em DateTimeOffset.")
    };

    /// <summary>Grava o valor como está, com <see cref="DbType.DateTimeOffset" /> — o mesmo que o Dapper faz sem handler.</summary>
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) {
        parameter.DbType = DbType.DateTimeOffset;
        parameter.Value = value;
    }
}
