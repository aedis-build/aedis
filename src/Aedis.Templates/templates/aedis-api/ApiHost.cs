using Aedis.Hosting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AedisApi1;

/// <summary>
///     Host da API. Por herdar de <see cref="AedisApiHost" />, já sobe com health probes
///     (<c>/health</c>, <c>/health/live</c>, <c>/health/ready</c>), security headers, rate limiting,
///     ProblemDetails (RFC 9457), validação 422 e o portão de autenticação fail-closed. Swagger é opt-in.
/// </summary>
public sealed class ApiHost : AedisApiHost
{
    // Para expor o Swagger em desenvolvimento, descomente:
    // protected override bool EnableSwagger => true;

    protected override void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration) =>
        services.AddAedisKeycloakAuth(configuration);

    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        // Registre aqui os serviços da sua aplicação.
    }

    protected override void ConfigureMiddleware(WebApplication app) {
        app.MapGet("/api/hello", () => Results.Ok(new { message = "Olá do Aedis" }))
           .RequireAuthorization();
    }
}
