using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Aedis.Database.Abstractions;
using Dapper;

namespace Aedis.Database.SqlServer;

/// <summary>
///     Tipa parâmetros <c>string</c> para o SQL Server: <c>varchar</c> por padrão e <c>nvarchar</c> quando a
///     propriedade tem <see cref="UnicodeAttribute" /> (ou quando <c>Database:SqlServerUnicodeStrings</c> inverte o
///     padrão), com o tamanho de <see cref="MaxLengthAttribute" />/<see cref="StringLengthAttribute" /> quando
///     declarado. Evita a conversão implícita da coluna (que descarta o índice) e o tamanho fixo de 4000 que o
///     Dapper usaria.
/// </summary>
internal static class SqlServerStrings
{
    private static readonly ConcurrentDictionary<PropertyInfo, (bool Unicode, int Length)> Cache = new();

    /// <summary>Parâmetro Dapper tipado para o valor de <paramref name="property" />.</summary>
    public static DbString Parameter(PropertyInfo property, string value, bool unicodeDefault) {
        var (unicode, length) = Cache.GetOrAdd(property, static p => (
            p.GetCustomAttribute<UnicodeAttribute>() is not null,
            p.GetCustomAttribute<MaxLengthAttribute>()?.Length ?? p.GetCustomAttribute<StringLengthAttribute>()?.MaximumLength ?? -1));
        return new DbString { Value = value, IsAnsi = !(unicode || unicodeDefault), Length = length };
    }
}
