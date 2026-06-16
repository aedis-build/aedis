using System.Data;
using Dapper;

namespace Aedis.Database.Postgres.TypeHandlers;

/// <summary>Dapper TypeHandler para <see cref="TimeOnly" /> ↔ coluna <c>time</c>.</summary>
public sealed class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public override TimeOnly Parse(object value) => value switch {
        TimeSpan ts => TimeOnly.FromTimeSpan(ts),
        DateTime dt => TimeOnly.FromDateTime(dt),
        TimeOnly t => t,
        string s => TimeOnly.Parse(s),
        _ => throw new InvalidCastException($"Não é possível converter {value.GetType()} em TimeOnly.")
    };

    public override void SetValue(IDbDataParameter parameter, TimeOnly value) {
        parameter.DbType = DbType.Time;
        parameter.Value = value.ToTimeSpan();
    }
}
