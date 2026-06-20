using Aedis.Diagnostics;
using Aedis.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Exemplo mínimo de uso de <see cref="AedisApiHost" /> — a documentação executável do host de API.
///     Uma aplicação real herda assim, registra seus serviços em <c>ConfigureServices</c> e mapeia seus
///     endpoints (aqui via minimal APIs em <c>ConfigureMiddleware</c>); toda a camada segura (headers, rate
///     limit, ProblemDetails, validação 422, health) já vem composta pelo host. No <c>Main</c> bastaria
///     <c>await new SampleApiHost().RunAsync(args);</c>.
/// </summary>
public sealed class SampleApiHost : AedisApiHost
{
    /// <summary>Exemplo sem provedor de identidade: roda aberto em Development (o portão fail-closed barraria em produção).</summary>
    protected override bool EnableAuthentication => false;

    /// <summary>Desliga a telemetria OTLP no exemplo/teste (evita exportador apontando para lugar nenhum).</summary>
    protected override bool EnableTelemetry => false;

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddScoped<IValidator<SampleInput>, SampleInputValidator>();
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }

    /// <inheritdoc />
    protected override void ConfigureMiddleware(WebApplication app) {
        app.MapGet("/ping", () => Results.Ok(new { message = "pong" }));
        app.MapGet("/conflito", () => { throw new BusinessException("registro já existe", ViolationType.ConflictError); });
        app.MapGet("/proibido", () => { throw new ForbiddenException("acesso negado"); });
        app.MapPost("/pedidos", (SampleInput input, IValidator<SampleInput> validator) => {
            var result = validator.Validate(input);
            if (!result.IsValid)
                throw new ValidationException(result.Errors);

            return Results.Ok(new { input.Nome });
        });
    }
}

/// <summary>Variante do exemplo com Swagger habilitado (opt-in), para demonstrar o documento OpenAPI.</summary>
public sealed class SwaggerSampleApiHost : AedisApiHost
{
    /// <inheritdoc cref="SampleApiHost.EnableAuthentication" />
    protected override bool EnableAuthentication => false;

    /// <inheritdoc cref="SampleApiHost.EnableTelemetry" />
    protected override bool EnableTelemetry => false;

    /// <summary>Liga o Swagger (no host real, mantenha desligado em produção ou proteja no ingress).</summary>
    protected override bool EnableSwagger => true;

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }
}

/// <summary>Modelo de entrada do exemplo, validado por <see cref="SampleInputValidator" />.</summary>
public sealed record SampleInput(string Nome);

/// <summary>Regra de validação do exemplo: o nome é obrigatório (falha → 422).</summary>
public sealed class SampleInputValidator : AbstractValidator<SampleInput>
{
    /// <summary>Define as regras de validação de <see cref="SampleInput" />.</summary>
    public SampleInputValidator() {
        RuleFor(input => input.Nome).NotEmpty();
    }
}
