using Aedis.Database.Abstractions;

namespace Aedis.Database.MySql;

/// <summary>
///     Compila uma <see cref="UpsertSpec" /> neutra para o dialeto do MySQL. Sem guarda, o upsert é um único
///     <c>INSERT … AS new ON DUPLICATE KEY UPDATE col = new.col</c> (alias de linha do MySQL 8.0.19+, que
///     substitui o <c>VALUES(col)</c> descontinuado). Com guarda, o MySQL não oferece um <c>WHERE</c> no
///     <c>ON DUPLICATE KEY UPDATE</c>, e um <c>IF(guarda, …)</c> por coluna seria incorreto: as atribuições
///     são avaliadas da esquerda para a direita e as seguintes já enxergam as colunas da guarda alteradas.
///     Por isso a guarda vira um <c>UPDATE … WHERE guarda</c> separado (<see cref="GuardedUpdate" />),
///     avaliado sobre a linha existente antes de qualquer atribuição, seguido do <c>INSERT</c> com sufixo
///     no-op (<c>chave = tabela.chave</c>) que só insere as linhas ausentes. O frescor é renderizado com
///     tratamento de nulo (<c>t.col IS NULL OR novo &gt;= t.col</c>) e o booleano com <c>true</c>/<c>false</c>
///     (<c>TINYINT(1)</c>) — em paridade semântica com o <c>ON CONFLICT … WHERE</c> do PostgreSQL e o
///     <c>MERGE</c> do SQL Server, a partir da mesma spec.
/// </summary>
internal static class MySqlUpsertCompiler
{
    /// <summary>
    ///     Sufixo <c>ON DUPLICATE KEY UPDATE</c> para um <c>INSERT … AS new</c>. É no-op
    ///     (<c>chave = tabela.chave</c>) quando a spec preserva a linha existente, quando não há coluna a
    ///     atualizar ou quando há guarda — neste último caso a atualização já foi feita por
    ///     <see cref="GuardedUpdate" />.
    /// </summary>
    public static string OnDuplicateKeyUpdate(UpsertSpec spec, string tableName, IReadOnlyList<string> properties,
        Func<string, string> column) {
        var sets = spec.UpdateMatched && !HasGuard(spec)
            ? UpdateSets(spec, properties, column, string.Empty, property => "new." + column(property))
            : [];
        var key = column(spec.KeyProperties[0]);
        return sets.Count == 0
            ? $"ON DUPLICATE KEY UPDATE {key} = {tableName}.{key}"
            : $"ON DUPLICATE KEY UPDATE {string.Join(", ", sets)}";
    }

    /// <summary>
    ///     <c>UPDATE</c> guardado que aplica a spec sobre a linha existente (alias <c>t</c>); <c>null</c> quando
    ///     a spec não tem guarda ou não há coluna a atualizar. Sem <paramref name="source" />, a linha que
    ///     entra vem de parâmetros e o comando casa a chave no <c>WHERE</c>; com <paramref name="source" />
    ///     (tabela de staging, alias <c>s</c>), casa a chave em um <c>JOIN</c>. <paramref name="incoming" />
    ///     diz como referenciar o valor que entra de uma propriedade (<c>@Prop</c> ou <c>s.col</c>).
    /// </summary>
    public static string? GuardedUpdate(UpsertSpec spec, string tableName, IReadOnlyList<string> properties,
        Func<string, string> column, Func<string, string> incoming, string? source) {
        if (!spec.UpdateMatched || !HasGuard(spec))
            return null;

        var sets = UpdateSets(spec, properties, column, "t.", incoming);
        if (sets.Count == 0)
            return null;

        var keyMatch = string.Join(" AND ", spec.KeyProperties.Select(k => $"t.{column(k)} = {incoming(k)}"));
        var guard = BuildGuard(spec.Guard!, column, incoming);
        return source is null
            ? $"UPDATE {tableName} t SET {string.Join(", ", sets)} WHERE {keyMatch} AND {guard}"
            : $"UPDATE {tableName} t JOIN {source} s ON {keyMatch} SET {string.Join(", ", sets)} WHERE {guard}";
    }

    private static bool HasGuard(UpsertSpec spec) => spec.Guard is { IsEmpty: false };

    private static List<string> UpdateSets(UpsertSpec spec, IReadOnlyList<string> properties,
        Func<string, string> column, string target, Func<string, string> incoming) {
        var keys = new HashSet<string>(spec.KeyProperties, StringComparer.OrdinalIgnoreCase);
        var preserved = new HashSet<string>(spec.PreservedProperties, StringComparer.OrdinalIgnoreCase);

        var sets = new List<string>();
        foreach (var property in properties.Where(p => !keys.Contains(p) && !preserved.Contains(p))) {
            sets.Add(spec.ServerValues.TryGetValue(property, out var serverValue)
                ? $"{target}{column(property)} = {ServerValue(serverValue)}"
                : $"{target}{column(property)} = {incoming(property)}");
        }

        return sets;
    }

    private static string BuildGuard(UpsertGuard guard, Func<string, string> column, Func<string, string> incoming) {
        var clauses = new List<string>();

        if (guard.Freshness.Count > 0) {
            var terms = guard.Freshness.Select(term => {
                var col = column(term.Property);
                var op = term.Operator == FreshnessOperator.Greater ? ">" : ">=";
                return $"(t.{col} IS NULL OR {incoming(term.Property)} {op} t.{col})";
            });
            clauses.Add($"({string.Join(" OR ", terms)})");
        }

        clauses.AddRange(guard.Booleans.Select(term =>
            $"t.{column(term.Property)} = {(term.ExpectedValue ? "true" : "false")}"));

        return string.Join(" AND ", clauses);
    }

    private static string ServerValue(UpsertServerValue value) => value switch {
        UpsertServerValue.UtcNow => "UTC_TIMESTAMP(6)",
        _ => throw new NotSupportedException($"Valor de servidor não suportado: {value}.")
    };
}
