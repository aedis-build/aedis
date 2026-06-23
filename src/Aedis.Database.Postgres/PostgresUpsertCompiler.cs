using System.Text;
using Aedis.Database.Abstractions;

namespace Aedis.Database.Postgres;

/// <summary>
///     Compila uma <see cref="UpsertSpec" /> neutra na cláusula <c>ON CONFLICT (…) DO UPDATE SET … WHERE …</c>
///     (ou <c>DO NOTHING</c>) do PostgreSQL. A linha que entra é referenciada por <c>EXCLUDED</c> e a
///     existente pelo nome da tabela; o frescor é renderizado com tratamento de nulo
///     (<c>tabela.col IS NULL OR EXCLUDED.col &gt;= tabela.col</c>) e o booleano com o literal
///     <c>false</c>/<c>true</c> — em paridade semântica com o MERGE do SQL Server, a partir da mesma spec.
/// </summary>
internal static class PostgresUpsertCompiler
{
    public static string Compile(UpsertSpec spec, string tableName, IReadOnlyList<string> allColumns,
        IReadOnlyList<string> keyColumns, Func<string, string> column) {
        var conflictTarget = $"ON CONFLICT ({string.Join(", ", keyColumns)})";
        if (!spec.UpdateMatched)
            return $"{conflictTarget} DO NOTHING";

        var keys = new HashSet<string>(keyColumns, StringComparer.OrdinalIgnoreCase);
        var sets = BuildUpdateSets(spec, allColumns, keys, column);
        if (sets.Count == 0)
            return $"{conflictTarget} DO NOTHING";

        var sql = $"{conflictTarget} DO UPDATE SET {string.Join(", ", sets)}";
        if (spec.Guard is { IsEmpty: false } guard)
            sql += " WHERE " + BuildGuard(guard, tableName, column);
        return sql;
    }

    private static List<string> BuildUpdateSets(UpsertSpec spec, IReadOnlyList<string> allColumns,
        HashSet<string> keys, Func<string, string> column) {
        var serverColumns = spec.ServerValues
            .ToDictionary(kvp => column(kvp.Key), kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        var preserved = new HashSet<string>(spec.PreservedProperties.Select(column), StringComparer.OrdinalIgnoreCase);

        var sets = new List<string>();
        foreach (var col in allColumns.Where(c => !keys.Contains(c) && !preserved.Contains(c))) {
            sets.Add(serverColumns.TryGetValue(col, out var serverValue)
                ? $"{col} = {ServerValue(serverValue)}"
                : $"{col} = EXCLUDED.{col}");
        }

        return sets;
    }

    private static string BuildGuard(UpsertGuard guard, string tableName, Func<string, string> column) {
        var clauses = new List<string>();

        if (guard.Freshness.Count > 0) {
            var terms = guard.Freshness.Select(term => {
                var col = column(term.Property);
                var op = term.Operator == FreshnessOperator.Greater ? ">" : ">=";
                return $"({tableName}.{col} IS NULL OR EXCLUDED.{col} {op} {tableName}.{col})";
            });
            clauses.Add($"({string.Join(" OR ", terms)})");
        }

        clauses.AddRange(guard.Booleans.Select(term =>
            $"{tableName}.{column(term.Property)} = {(term.ExpectedValue ? "true" : "false")}"));

        return string.Join(" AND ", clauses);
    }

    private static string ServerValue(UpsertServerValue value) => value switch {
        UpsertServerValue.UtcNow => "now()",
        _ => throw new NotSupportedException($"Valor de servidor não suportado: {value}.")
    };
}
