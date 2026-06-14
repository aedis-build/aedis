using System.Text;

namespace Aedis.Exceptions;

/// <summary>
///     Extensões para <see cref="ViolationType" />.
/// </summary>
public static class ViolationTypeExtensions
{
    /// <summary>
    ///     Converte o <see cref="ViolationType" /> para string em formato snake_case.
    ///     Usado para serialização JSON e compatibilidade com o padrão PayHop.
    /// </summary>
    /// <param name="violationType">Tipo de violação a ser convertido.</param>
    /// <returns>String em formato snake_case (ex: "validation_error", "foreign_key_violation").</returns>
    public static string ToSnakeCase(this ViolationType violationType) {
        return violationType switch {
            ViolationType.ValidationError => "validation_error",
            ViolationType.ForeignKeyViolation => "foreign_key_violation",
            ViolationType.UniqueConstraintViolation => "unique_constraint_violation",
            ViolationType.ConflictError => "conflict_error",
            ViolationType.PreconditionFailed => "precondition_failed",
            ViolationType.BusinessError => "business_error",
            _ => ConvertToSnakeCase(violationType.ToString())
        };
    }

    /// <summary>
    ///     Converte string em formato snake_case para <see cref="ViolationType" />.
    ///     Útil para deserialização ou conversão de valores legados.
    /// </summary>
    /// <param name="snakeCase">String em formato snake_case (ex: "validation_error").</param>
    /// <returns>ViolationType correspondente ou <see cref="ViolationType.ValidationError" /> se não encontrado.</returns>
    public static ViolationType FromSnakeCase(string snakeCase) {
        if (string.IsNullOrWhiteSpace(snakeCase)) return ViolationType.ValidationError;

        return snakeCase.ToLowerInvariant() switch {
            "validation_error" => ViolationType.ValidationError,
            "foreign_key_violation" => ViolationType.ForeignKeyViolation,
            "unique_constraint_violation" => ViolationType.UniqueConstraintViolation,
            "conflict_error" => ViolationType.ConflictError,
            "precondition_failed" => ViolationType.PreconditionFailed,
            "business_error" => ViolationType.BusinessError,
            _ => ViolationType.ValidationError // Default
        };
    }

    /// <summary>
    ///     Converte PascalCase para snake_case genérico.
    ///     Fallback para valores não mapeados explicitamente.
    /// </summary>
    private static string ConvertToSnakeCase(string pascalCase) {
        if (string.IsNullOrEmpty(pascalCase)) return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < pascalCase.Length; i++) {
            if (i > 0 && char.IsUpper(pascalCase[i])) sb.Append('_');

            sb.Append(char.ToLowerInvariant(pascalCase[i]));
        }

        return sb.ToString();
    }
}