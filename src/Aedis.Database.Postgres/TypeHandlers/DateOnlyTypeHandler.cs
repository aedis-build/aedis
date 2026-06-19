using System.Data;
using Dapper;

namespace Aedis.Database.Postgres.TypeHandlers;

/// <summary>Dapper TypeHandler para <see cref="DateOnly" /> ↔ coluna <c>date</c>.</summary>
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    /// <summary>Lê o valor vindo do banco (<c>DateTime</c>, <c>string</c>…) e o converte em <see cref="DateOnly" />.</summary>
    public override DateOnly Parse(object value) => value switch {
        DateTime dt => DateOnly.FromDateTime(dt),
        DateOnly d => d,
        string s => DateOnly.Parse(s),
        _ => throw new InvalidCastException($"Não é possível converter {value.GetType()} em DateOnly.")
    };

    /// <summary>Grava o <see cref="DateOnly" /> no parâmetro como <c>date</c> (<c>DateTime</c> à meia-noite), para envio ao PostgreSQL.</summary>
    public override void SetValue(IDbDataParameter parameter, DateOnly value) {
        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }
}
