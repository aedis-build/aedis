using System.Text;
using System.Text.RegularExpressions;
using Aedis.Database.Abstractions;

namespace Aedis.Database.Postgres.Naming;

/// <summary>
///     Estratégia <c>snake_case</c> (idiomática no PostgreSQL): tabelas pluralizadas em snake, colunas em
///     snake, índices <c>idx_tabela_colunas</c> e constraints <c>prefixo_tabela_colunas</c>.
/// </summary>
public sealed partial class SnakeCaseNamingStrategy : INamingStrategy
{
    /// <inheritdoc />
    public bool CanHandle(NamingContext context) => context.Convention == NamingConvention.SnakeCase;

    /// <inheritdoc />
    public Task ExecuteAsync(NamingContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public string Convert(NamingContext context) => context.Operation switch {
        NamingOperation.TableName => Pluralize(ToSnakeCase(context.Input)),
        NamingOperation.ColumnName => ToSnakeCase(context.Input),
        NamingOperation.IndexName => BuildIndexName(context),
        NamingOperation.ConstraintName => BuildConstraintName(context),
        _ => ToSnakeCase(context.Input)
    };

    /// <inheritdoc />
    public bool Validate(NamingContext context, out string? errorMessage) {
        if (SnakeCasePattern().IsMatch(context.Input)) {
            errorMessage = null;
            return true;
        }

        var kind = context.Operation == NamingOperation.TableName ? "Tabela" : "Coluna";
        errorMessage = $"O nome '{context.Input}' de {kind} deve estar em snake_case (minúsculas com underscores).";
        return false;
    }

    private static string BuildIndexName(NamingContext context) {
        var columns = string.Join("_", (context.AdditionalParameters ?? []).Select(ToSnakeCase));
        return $"idx_{ToSnakeCase(context.Input)}_{columns}";
    }

    private static string BuildConstraintName(NamingContext context) {
        var parts = context.Input.Split('|');
        var prefix = parts[0];
        var table = ToSnakeCase(parts[1]);
        if (context.AdditionalParameters is null or { Length: 0 })
            return $"{prefix}_{table}";
        return $"{prefix}_{table}_{string.Join("_", context.AdditionalParameters.Select(ToSnakeCase))}";
    }

    internal static string ToSnakeCase(string name) {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new StringBuilder();
        sb.Append(char.ToLowerInvariant(name[0]));
        for (var i = 1; i < name.Length; i++) {
            var c = name[i];
            if (char.IsUpper(c)) {
                sb.Append('_').Append(char.ToLowerInvariant(c));
            }
            else {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string Pluralize(string name) {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.EndsWith('s') || name.EndsWith('x') || name.EndsWith('z')
            || name.EndsWith("ch") || name.EndsWith("sh"))
            return name + "es";
        if (name.EndsWith('y') && name.Length > 1 && !"aeiou".Contains(char.ToLowerInvariant(name[^2])))
            return name[..^1] + "ies";
        return name + "s";
    }

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$")]
    private static partial Regex SnakeCasePattern();
}

/// <summary>Estratégia <c>PascalCase</c> — tabelas pluralizadas, colunas e demais nomes em PascalCase.</summary>
public sealed class PascalCaseNamingStrategy : INamingStrategy
{
    /// <inheritdoc />
    public bool CanHandle(NamingContext context) => context.Convention == NamingConvention.PascalCase;

    /// <inheritdoc />
    public Task ExecuteAsync(NamingContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public string Convert(NamingContext context) {
        var pascal = ToPascalCase(context.Input);
        return context.Operation switch {
            NamingOperation.TableName => pascal.EndsWith('s') ? pascal : pascal + "s",
            _ => pascal
        };
    }

    /// <inheritdoc />
    public bool Validate(NamingContext context, out string? errorMessage) {
        errorMessage = null;
        return true;
    }

    internal static string ToPascalCase(string name) {
        if (string.IsNullOrEmpty(name)) return name;
        var snake = SnakeCaseNamingStrategy.ToSnakeCase(name);
        var sb = new StringBuilder();
        foreach (var part in snake.Split('_', StringSplitOptions.RemoveEmptyEntries))
            sb.Append(char.ToUpperInvariant(part[0])).Append(part[1..]);
        return sb.ToString();
    }
}

/// <summary>Estratégia <c>camelCase</c> — como PascalCase, mas com a primeira letra minúscula.</summary>
public sealed class CamelCaseNamingStrategy : INamingStrategy
{
    /// <inheritdoc />
    public bool CanHandle(NamingContext context) => context.Convention == NamingConvention.CamelCase;

    /// <inheritdoc />
    public Task ExecuteAsync(NamingContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public string Convert(NamingContext context) {
        var pascal = PascalCaseNamingStrategy.ToPascalCase(context.Input);
        var camel = string.IsNullOrEmpty(pascal) ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];
        return context.Operation switch {
            NamingOperation.TableName => camel.EndsWith('s') ? camel : camel + "s",
            _ => camel
        };
    }

    /// <inheritdoc />
    public bool Validate(NamingContext context, out string? errorMessage) {
        errorMessage = null;
        return true;
    }
}
