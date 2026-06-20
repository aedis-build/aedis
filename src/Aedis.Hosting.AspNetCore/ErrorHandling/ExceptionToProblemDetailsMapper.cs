using System.Security;
using Aedis.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aedis.Hosting.AspNetCore.ErrorHandling;

/// <summary>
///     Traduz exceções em <see cref="ProblemDetails" /> com o status HTTP e o detalhe seguros. Exceções de
///     negócio expõem sua mensagem (são destinadas ao cliente); exceções inesperadas viram um 500 genérico
///     <em>sem</em> revelar mensagem ou stack trace, evitando vazamento de informação (OWASP A05/A09).
/// </summary>
public static class ExceptionToProblemDetailsMapper
{
    /// <summary>
    ///     Mapeia <paramref name="exception" /> para um <see cref="ProblemDetails" /> usando a
    ///     <paramref name="factory" />. O status segue o tipo da exceção: <see cref="BusinessException" /> usa
    ///     seu status efetivo; validação → 422; <see cref="ForbiddenException" /> → 403; falta de autenticação
    ///     → 401; indisponibilidade temporária → 503; qualquer outra → 500 genérico.
    /// </summary>
    public static ProblemDetails Map(HttpContext context, Exception exception, IProblemDetailsFactory factory) {
        return exception switch {
            BusinessException business => factory.Create(
                context,
                business.EffectiveStatusCode,
                StatusTitle(business.EffectiveStatusCode),
                business.Message,
                extensions: BusinessExtensions(business)),

            ValidationException validation => factory.Create(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "Unprocessable Entity",
                "Uma ou mais regras de validação falharam.",
                extensions: ValidationExtensions(validation)),

            ForbiddenException forbidden => factory.Create(
                context, StatusCodes.Status403Forbidden, "Forbidden", forbidden.Message),

            ServiceTemporarilyUnavailableException => factory.Create(
                context, StatusCodes.Status503ServiceUnavailable, "Service Unavailable",
                "O serviço está temporariamente indisponível. Tente novamente em instantes."),

            UnauthorizedAccessException or SecurityException => factory.Create(
                context, StatusCodes.Status401Unauthorized, "Unauthorized", "Autenticação necessária."),

            _ => factory.Create(
                context, StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Ocorreu um erro inesperado.")
        };
    }

    /// <summary>Título HTTP estável para os status que a plataforma produz.</summary>
    public static string StatusTitle(int statusCode) => statusCode switch {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status412PreconditionFailed => "Precondition Failed",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
        >= 500 => "Internal Server Error",
        _ => "Error"
    };

    private static Dictionary<string, object?> BusinessExtensions(BusinessException business) => new() {
        ["category"] = business.Category,
        ["violationType"] = business.ViolationType.ToString(),
        ["rule"] = business.Rule
    };

    private static Dictionary<string, object?> ValidationExtensions(ValidationException validation) {
        var errors = validation.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());

        return new Dictionary<string, object?> { ["errors"] = errors };
    }
}
