using Aedis.Database.Abstractions;
using Npgsql;

namespace Aedis.Database.Postgres;

/// <summary>
///     Normaliza exceções do Npgsql em <see cref="DatabaseError" /> a partir do <c>SQLSTATE</c> e dos campos
///     estruturados que o PostgreSQL devolve (restrição, coluna, tabela), sem interpretar mensagens de texto.
/// </summary>
internal static class PostgresDatabaseErrorTranslator
{
    internal const string ProviderName = "postgres";

    /// <summary>Traduz a exceção; <c>null</c> quando ela não é um erro do banco reconhecido.</summary>
    internal static DatabaseError? TryTranslate(Exception exception) {
        return exception switch {
            PostgresException postgres => new DatabaseError(
                KindFor(postgres.SqlState), ProviderName, postgres.SqlState, null,
                postgres.ConstraintName, postgres.ColumnName, postgres.TableName, postgres.MessageText, postgres),
            NpgsqlException { IsTransient: true } transient => new DatabaseError(
                DatabaseErrorKind.ConnectionFailure, ProviderName, transient.SqlState, null,
                null, null, null, transient.Message, transient),
            TimeoutException timeout => new DatabaseError(
                DatabaseErrorKind.Timeout, ProviderName, null, null, null, null, null, timeout.Message, timeout),
            _ => null
        };
    }

    private static DatabaseErrorKind KindFor(string? sqlState) {
        return sqlState switch {
            "23505" => DatabaseErrorKind.UniqueViolation,
            "23503" => DatabaseErrorKind.ForeignKeyViolation,
            "23514" => DatabaseErrorKind.CheckViolation,
            "23P01" => DatabaseErrorKind.ExclusionViolation,
            "23502" => DatabaseErrorKind.NotNullViolation,
            "40001" => DatabaseErrorKind.SerializationFailure,
            "40P01" => DatabaseErrorKind.Deadlock,
            "57014" => DatabaseErrorKind.Timeout,
            { Length: >= 2 } when sqlState.StartsWith("08", StringComparison.Ordinal) => DatabaseErrorKind.ConnectionFailure,
            _ => DatabaseErrorKind.Other
        };
    }
}
