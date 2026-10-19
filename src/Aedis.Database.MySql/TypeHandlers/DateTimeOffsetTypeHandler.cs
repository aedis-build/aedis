using System.Data;
using Dapper;

namespace Aedis.Database.MySql.TypeHandlers;

/// <summary>
///     Dapper TypeHandler para <see cref="DateTimeOffset" /> ↔ coluna <c>DATETIME(6)</c>. O MySQL não guarda
///     offset, então o valor é gravado convertido para UTC e lido de volta com offset zero — o
///     <see cref="DateTime" /> devolvido pelo driver (<c>Unspecified</c> por padrão) é interpretado como UTC;
///     um <see cref="DateTime" /> <c>Local</c> (opção <c>DateTimeKind=Local</c> do driver) é convertido antes.
///     Sem o handler, o Dapper não casa a coluna com uma propriedade <see cref="DateTimeOffset" />.
/// </summary>
public sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    /// <summary>Converte o valor lido (<see cref="DateTime" /> ou <see cref="DateTimeOffset" />) em <see cref="DateTimeOffset" /> UTC.</summary>
    public override DateTimeOffset Parse(object value) => value switch {
        DateTimeOffset dto => dto,
        DateTime { Kind: DateTimeKind.Local } local => new DateTimeOffset(local.ToUniversalTime(), TimeSpan.Zero),
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero),
        _ => throw new InvalidCastException($"Não é possível converter {value.GetType()} em DateTimeOffset.")
    };

    /// <summary>Grava o instante convertido para UTC como <see cref="DbType.DateTime" /> (<c>DATETIME(6)</c>).</summary>
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) {
        parameter.DbType = DbType.DateTime;
        parameter.Value = value.UtcDateTime;
    }
}
