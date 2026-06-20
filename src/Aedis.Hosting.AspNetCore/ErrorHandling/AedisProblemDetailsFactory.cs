using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aedis.Hosting.AspNetCore.ErrorHandling;

/// <summary>
///     Implementação padrão de <see cref="IProblemDetailsFactory" />. Preenche <c>traceId</c> com o id do
///     trace ativo (<c>Activity.Current</c>) ou, na ausência, com o <c>TraceIdentifier</c> da conexão,
///     viabilizando correlação ponta-a-ponta entre a resposta de erro, os logs e as métricas.
/// </summary>
public sealed class AedisProblemDetailsFactory : IProblemDetailsFactory
{
    /// <inheritdoc />
    public ProblemDetails Create(
        HttpContext context,
        int statusCode,
        string title,
        string? detail = null,
        string? type = null,
        IReadOnlyDictionary<string, object?>? extensions = null) {
        var problem = new ProblemDetails {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = type ?? $"https://httpstatuses.io/{statusCode}",
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        if (extensions is not null)
            foreach (var (key, value) in extensions)
                problem.Extensions[key] = value;

        return problem;
    }
}
