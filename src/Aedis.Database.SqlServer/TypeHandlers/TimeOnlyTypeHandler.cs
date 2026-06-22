using System.Data;
using Dapper;

namespace Aedis.Database.SqlServer.TypeHandlers;

/// <summary>Dapper TypeHandler para <see cref="TimeOnly" /> ↔ coluna <c>time</c>.</summary>
public sealed class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    /// <summary>Lê o valor vindo do banco (<c>TimeSpan</c>, <c>DateTime</c>, <c>string</c>…) e o converte em <see cref="TimeOnly" />.</summary>
    public override TimeOnly Parse(object value) => value switch {
        TimeSpan ts => TimeOnly.FromTimeSpan(ts),
        DateTime dt => TimeOnly.FromDateTime(dt),
        TimeOnly t => t,
        string s => TimeOnly.Parse(s),
        _ => throw new InvalidCastException($"Não é possível converter {value.GetType()} em TimeOnly.")
    };

    /// <summary>Grava o <see cref="TimeOnly" /> no parâmetro como <c>time</c> (<c>TimeSpan</c>), para envio ao banco.</summary>
    public override void SetValue(IDbDataParameter parameter, TimeOnly value) {
        parameter.DbType = DbType.Time;
        parameter.Value = value.ToTimeSpan();
    }
}
