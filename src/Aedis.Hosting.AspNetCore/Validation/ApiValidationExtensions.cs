using System.Reflection;
using Aedis.Hosting.AspNetCore.ErrorHandling;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Liga a validação de entrada do Aedis: auto-validação do FluentValidation com descoberta de validators
///     por assembly e conversão de estado de modelo inválido em <c>422 Unprocessable Entity</c> no formato
///     ProblemDetails (campo <c>errors</c> por propriedade). Cobre OWASP A03 (validação de entrada).
/// </summary>
public static class ApiValidationExtensions
{
    private const string ProblemJsonContentType = "application/problem+json";

    /// <summary>
    ///     Registra a auto-validação do FluentValidation, descobre os validators do
    ///     <paramref name="validatorsAssembly" /> (ou do assembly de entrada, quando nulo) e configura a
    ///     resposta 422 padronizada para modelo inválido. Requer <c>AddAedisProblemDetails</c>.
    /// </summary>
    public static IServiceCollection AddAedisApiValidation(this IServiceCollection services, Assembly? validatorsAssembly = null) {
        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssembly(validatorsAssembly ?? Assembly.GetEntryAssembly()!);

        services.Configure<ApiBehaviorOptions>(options => {
            options.InvalidModelStateResponseFactory = context => {
                var factory = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsFactory>();

                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray());

                var problem = factory.Create(
                    context.HttpContext,
                    StatusCodes.Status422UnprocessableEntity,
                    "Unprocessable Entity",
                    "Uma ou mais regras de validação falharam.",
                    extensions: new Dictionary<string, object?> { ["errors"] = errors });

                return new ObjectResult(problem) {
                    StatusCode = StatusCodes.Status422UnprocessableEntity,
                    ContentTypes = { ProblemJsonContentType }
                };
            };
        });

        return services;
    }
}
