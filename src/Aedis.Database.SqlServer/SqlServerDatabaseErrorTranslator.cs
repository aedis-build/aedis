using System.Text.RegularExpressions;
using Aedis.Database.Abstractions;
using Microsoft.Data.SqlClient;

namespace Aedis.Database.SqlServer;

/// <summary>
///     Normaliza exceções do SqlClient em <see cref="DatabaseError" /> a partir do <c>Number</c> do erro. O SQL
///     Server não expõe o nome da restrição como campo estruturado, então ele é extraído da mensagem apenas no
///     formato fixo <c>constraint "Nome"</c>/<c>'Nome'</c> que o servidor emite.
/// </summary>
internal static partial class SqlServerDatabaseErrorTranslator
{
    internal const string ProviderName = "sqlserver";

    /// <summary>Traduz a exceção; <c>null</c> quando ela não é um erro do banco reconhecido.</summary>
    internal static DatabaseError? TryTranslate(Exception exception) {
        return exception switch {
            SqlException sql => new DatabaseError(
                KindFor(sql.Number, sql.Message), ProviderName, null, sql.Number,
                ExtractConstraintName(sql.Message), null, null, sql.Message, sql),
            TimeoutException timeout => new DatabaseError(
                DatabaseErrorKind.Timeout, ProviderName, null, null, null, null, null, timeout.Message, timeout),
            _ => null
        };
    }

    private static DatabaseErrorKind KindFor(int number, string message) {
        return number switch {
            2627 or 2601 => DatabaseErrorKind.UniqueViolation,
            547 when message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) => DatabaseErrorKind.ForeignKeyViolation,
            547 => DatabaseErrorKind.CheckViolation,
            515 => DatabaseErrorKind.NotNullViolation,
            1205 => DatabaseErrorKind.Deadlock,
            3960 => DatabaseErrorKind.SerializationFailure,
            -2 => DatabaseErrorKind.Timeout,
            53 or 4060 or 10054 or 10060 or 18456 or 40613 => DatabaseErrorKind.ConnectionFailure,
            _ => DatabaseErrorKind.Other
        };
    }

    private static string? ExtractConstraintName(string message) {
        var match = ConstraintName().Match(message);
        return match.Success ? match.Groups["name"].Value : null;
    }

    [GeneratedRegex(@"constraint ""?'?(?<name>[^""'.\s]+)""?'?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConstraintName();
}
