using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Aedis.Hosting.AspNetCore.ErrorHandling;

/// <summary>
///     Captura qualquer exceção não tratada do pipeline, registra-a (com stack trace nos logs) e responde
///     <c>application/problem+json</c> conforme <see cref="ExceptionToProblemDetailsMapper" /> — sem expor a
///     stack ao cliente. Erros 5xx são logados como Error (anomalia); violações de negócio (4xx) como Debug,
///     por serem esperadas. Deve ser o primeiro middleware do pipeline para envolver todos os demais.
/// </summary>
public sealed class GlobalExceptionHandlingMiddleware
{
    private const string ProblemJsonContentType = "application/problem+json";

    private readonly RequestDelegate _next;
    private readonly IProblemDetailsFactory _problemDetailsFactory;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

    /// <summary>Cria o middleware com o próximo elo, a fábrica de ProblemDetails e o logger.</summary>
    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        IProblemDetailsFactory problemDetailsFactory,
        ILogger<GlobalExceptionHandlingMiddleware> logger) {
        _next = next;
        _problemDetailsFactory = problemDetailsFactory;
        _logger = logger;
    }

    /// <summary>Executa o pipeline e converte qualquer exceção em uma resposta ProblemDetails.</summary>
    public async Task InvokeAsync(HttpContext context) {
        try {
            await _next(context);
        }
        catch (Exception exception) {
            await HandleAsync(context, exception);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception) {
        var problem = ExceptionToProblemDetailsMapper.Map(context, exception, _problemDetailsFactory);
        var statusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        if (statusCode >= StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Erro não tratado ao processar {Method} {Path}.", context.Request.Method, context.Request.Path);
        else
            _logger.LogDebug(exception, "Requisição rejeitada ({StatusCode}) em {Method} {Path}.", statusCode, context.Request.Method, context.Request.Path);

        if (context.Response.HasStarted) {
            _logger.LogWarning("A resposta já havia começado; não foi possível escrever o ProblemDetails.");
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: ProblemJsonContentType);
    }
}
