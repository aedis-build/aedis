using System.Text.RegularExpressions;
using Aedis.Database.Abstractions;
using MySqlConnector;

namespace Aedis.Database.MySql;

/// <summary>
///     Normaliza exceções do MySqlConnector em <see cref="DatabaseError" /> a partir do <c>Number</c> do erro
///     do servidor. O MySQL não expõe o nome da restrição como campo estruturado, então ele é extraído da
///     mensagem apenas nos formatos fixos que o servidor emite (<c>for key 'tabela.nome'</c>,
///     <c>CONSTRAINT `nome`</c> e <c>Check constraint 'nome'</c>); a coluna vem de <c>Column 'nome'</c>.
///     Falhas de rede (2002/2003/2006/2013) e qualquer erro marcado como transiente pelo driver viram
///     <see cref="DatabaseErrorKind.ConnectionFailure" />; <c>lock wait timeout</c> (1205) vira
///     <see cref="DatabaseErrorKind.Timeout" />.
/// </summary>
internal static class MySqlDatabaseErrorTranslator
{
    internal const string ProviderName = "mysql";

    private static readonly Regex ConstraintName = new(
        @"for key '(?:[^'.]+\.)?(?<name>[^']+)'|CONSTRAINT `(?<name>[^`]+)`|Check constraint '(?<name>[^']+)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex ColumnName = new(@"Column '(?<name>[^']+)'", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Traduz a exceção; <c>null</c> quando ela não é um erro do banco reconhecido.</summary>
    internal static DatabaseError? TryTranslate(Exception exception) {
        return exception switch {
            MySqlException mysql => new DatabaseError(
                KindFor(mysql), ProviderName, mysql.SqlState, mysql.Number,
                Extract(ConstraintName, mysql.Message), Extract(ColumnName, mysql.Message), null, mysql.Message, mysql),
            TimeoutException timeout => new DatabaseError(
                DatabaseErrorKind.Timeout, ProviderName, null, null, null, null, null, timeout.Message, timeout),
            _ => null
        };
    }

    private static DatabaseErrorKind KindFor(MySqlException exception) {
        return exception.Number switch {
            1062 => DatabaseErrorKind.UniqueViolation,
            1451 or 1452 => DatabaseErrorKind.ForeignKeyViolation,
            3819 => DatabaseErrorKind.CheckViolation,
            1048 or 1364 => DatabaseErrorKind.NotNullViolation,
            1213 => DatabaseErrorKind.Deadlock,
            1205 => DatabaseErrorKind.Timeout,
            2002 or 2003 or 2006 or 2013 => DatabaseErrorKind.ConnectionFailure,
            _ when exception.IsTransient => DatabaseErrorKind.ConnectionFailure,
            _ => DatabaseErrorKind.Other
        };
    }

    private static string? Extract(Regex pattern, string message) {
        var match = pattern.Match(message);
        return match.Success ? match.Groups["name"].Value : null;
    }
}
