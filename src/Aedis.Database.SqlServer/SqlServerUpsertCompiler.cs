using System.Text;
using Aedis.Database.Abstractions;

namespace Aedis.Database.SqlServer;

/// <summary>
///     Compila uma <see cref="UpsertSpec" /> neutra no corpo de um <c>MERGE</c> do SQL Server — as cláusulas
///     <c>WHEN MATCHED [AND &lt;guarda&gt;] THEN UPDATE SET …</c> e <c>WHEN NOT MATCHED BY TARGET THEN
///     INSERT …</c>. A linha que entra é referenciada por <c>s.</c> (source) e a existente por <c>t.</c>
///     (target); o frescor é renderizado com tratamento de nulo (<c>t.[c] IS NULL OR s.[c] &gt;= t.[c]</c>)
///     e o booleano com o literal <c>0</c>/<c>1</c> do SQL Server. O prefixo
///     (<c>MERGE INTO … USING … ON …</c>) é montado por quem chama (staging no bulk, <c>VALUES</c> no Save).
/// </summary>
internal static class SqlServerUpsertCompiler
{
    public static string BuildMergeTail(UpsertSpec spec, IReadOnlyList<string> allColumns,
        IReadOnlyList<string> keyColumns, Func<string, string> column) {
        var keys = new HashSet<string>(keyColumns, StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();

        if (spec.UpdateMatched) {
            var sets = BuildUpdateSets(spec, allColumns, keys, column);
            if (sets.Count > 0) {
                builder.Append("WHEN MATCHED");
                if (spec.Guard is { IsEmpty: false } guard)
                    builder.Append(" AND ").Append(BuildGuard(guard, column));
                builder.Append(" THEN UPDATE SET ").Append(string.Join(", ", sets)).Append(' ');
            }
        }

        var insertColumns = string.Join(", ", allColumns.Select(c => $"[{c}]"));
        var insertValues = string.Join(", ", allColumns.Select(c => $"s.[{c}]"));
        builder.Append($"WHEN NOT MATCHED BY TARGET THEN INSERT ({insertColumns}) VALUES ({insertValues});");
        return builder.ToString();
    }

    private static List<string> BuildUpdateSets(UpsertSpec spec, IReadOnlyList<string> allColumns,
        HashSet<string> keys, Func<string, string> column) {
        var serverColumns = spec.ServerValues
            .ToDictionary(kvp => column(kvp.Key), kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        var preserved = new HashSet<string>(spec.PreservedProperties.Select(column), StringComparer.OrdinalIgnoreCase);

        var sets = new List<string>();
        foreach (var col in allColumns.Where(c => !keys.Contains(c) && !preserved.Contains(c))) {
            sets.Add(serverColumns.TryGetValue(col, out var serverValue)
                ? $"t.[{col}] = {ServerValue(serverValue)}"
                : $"t.[{col}] = s.[{col}]");
        }

        return sets;
    }

    private static string BuildGuard(UpsertGuard guard, Func<string, string> column) {
        var clauses = new List<string>();

        if (guard.Freshness.Count > 0) {
            var terms = guard.Freshness.Select(term => {
                var col = column(term.Property);
                var op = term.Operator == FreshnessOperator.Greater ? ">" : ">=";
                return $"(t.[{col}] IS NULL OR s.[{col}] {op} t.[{col}])";
            });
            clauses.Add($"({string.Join(" OR ", terms)})");
        }

        clauses.AddRange(guard.Booleans.Select(term =>
            $"t.[{column(term.Property)}] = {(term.ExpectedValue ? "1" : "0")}"));

        return string.Join(" AND ", clauses);
    }

    private static string ServerValue(UpsertServerValue value) => value switch {
        UpsertServerValue.UtcNow => "SYSUTCDATETIME()",
        _ => throw new NotSupportedException($"Valor de servidor não suportado: {value}.")
    };
}
