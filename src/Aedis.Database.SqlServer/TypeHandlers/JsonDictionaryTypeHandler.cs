using System.Data;
using System.Text.Json;
using Dapper;

namespace Aedis.Database.SqlServer.TypeHandlers;

/// <summary>
///     Dapper TypeHandler para dicionários <c>string → string</c> ↔ coluna <c>nvarchar(max)</c> com JSON. Registre
///     uma instância por tipo exato usado nas entidades (<see cref="Dictionary{TKey,TValue}" />,
///     <see cref="IDictionary{TKey,TValue}" />, <see cref="IReadOnlyDictionary{TKey,TValue}" />): o Dapper casa
///     handler por tipo, não por atribuição. Nulo ou vazio no banco vira nulo; nulo na entidade vira NULL.
/// </summary>
/// <typeparam name="T">Tipo do dicionário na entidade.</typeparam>
public sealed class JsonDictionaryTypeHandler<T> : SqlMapper.TypeHandler<T> where T : class, IEnumerable<KeyValuePair<string, string?>>
{
    /// <summary>Lê o JSON vindo do banco (<c>string</c>) e o converte no dicionário.</summary>
    public override T? Parse(object value) => value switch {
        null or DBNull => null,
        string { Length: 0 } => null,
        string json => JsonSerializer.Deserialize<Dictionary<string, string?>>(json) as T
                       ?? throw new InvalidCastException($"Não é possível converter o JSON em {typeof(T)}."),
        _ => throw new InvalidCastException($"Não é possível converter {value.GetType()} em {typeof(T)}.")
    };

    /// <summary>Grava o dicionário no parâmetro como texto JSON.</summary>
    public override void SetValue(IDbDataParameter parameter, T? value) {
        parameter.DbType = DbType.String;
        parameter.Value = value is null ? DBNull.Value : Serialize(value);
    }

    /// <summary>Serializa o dicionário no JSON gravado no banco.</summary>
    public static string Serialize(IEnumerable<KeyValuePair<string, string?>> value) =>
        JsonSerializer.Serialize(value is IDictionary<string, string?> d ? d : value.ToDictionary(p => p.Key, p => p.Value));
}
