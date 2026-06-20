using Aedis.Hosting.AspNetCore.ErrorHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Registra e liga o tratamento de erros do Aedis: a fábrica de ProblemDetails, o middleware global de
///     exceções e a conversão de respostas de status-only de erro (ex.: 401/403/404 sem corpo) em
///     <c>application/problem+json</c>. Garante respostas de erro consistentes e sem vazamento de stack.
/// </summary>
public static class ErrorHandlingExtensions
{
    private const string ProblemJsonContentType = "application/problem+json";

    /// <summary>Registra <see cref="IProblemDetailsFactory" /> no contêiner (idempotente).</summary>
    public static IServiceCollection AddAedisProblemDetails(this IServiceCollection services) {
        services.TryAddSingleton<IProblemDetailsFactory, AedisProblemDetailsFactory>();
        return services;
    }

    /// <summary>
    ///     Liga o tratamento de erros no pipeline: o middleware global de exceções seguido da conversão de
    ///     respostas de erro sem corpo em ProblemDetails. Coloque cedo, antes da autenticação.
    /// </summary>
    public static IApplicationBuilder UseAedisExceptionHandling(this IApplicationBuilder app) {
        app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

        app.UseStatusCodePages(async context => {
            var response = context.HttpContext.Response;
            if (response.HasStarted || response.ContentLength.HasValue || response.StatusCode < StatusCodes.Status400BadRequest)
                return;

            var factory = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsFactory>();
            var problem = factory.Create(context.HttpContext, response.StatusCode, ExceptionToProblemDetailsMapper.StatusTitle(response.StatusCode));

            await response.WriteAsJsonAsync(problem, options: null, contentType: ProblemJsonContentType);
        });

        return app;
    }
}
