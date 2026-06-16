using System.Text.RegularExpressions;

namespace Aedis.Database.Postgres.Queries;

/// <summary>
///     Guarda para <em>identificadores</em> SQL (nomes de tabela/coluna/ordenação). Diferente de valores
///     — que são sempre enviados como <em>bind parameters</em> e por isso já são imunes a injeção — um
///     identificador não pode ser parametrizado; quando vier de origem dinâmica, valide-o aqui. Aceita
///     apenas identificadores simples (<c>letra/underscore</c> seguido de letras, dígitos ou underscores),
///     opcionalmente qualificados por schema (<c>schema.tabela</c>). Qualquer outra coisa (espaços,
///     aspas, ponto-e-vírgula, comentários, parênteses) é rejeitada.
/// </summary>
public static partial class SqlIdentifier
{
    /// <summary>Valida e devolve o identificador; lança <see cref="ArgumentException" /> se for inseguro.</summary>
    public static string Validate(string identifier) {
        if (string.IsNullOrWhiteSpace(identifier) || !IdentifierPattern().IsMatch(identifier))
            throw new ArgumentException($"Identificador SQL inválido ou potencialmente inseguro: '{identifier}'.",
                nameof(identifier));
        return identifier;
    }

    /// <summary>Indica se o identificador é seguro, sem lançar.</summary>
    public static bool IsValid(string? identifier) =>
        !string.IsNullOrWhiteSpace(identifier) && IdentifierPattern().IsMatch(identifier);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$")]
    private static partial Regex IdentifierPattern();
}
