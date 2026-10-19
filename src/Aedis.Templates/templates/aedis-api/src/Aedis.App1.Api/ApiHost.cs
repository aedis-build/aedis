using Aedis.App1.Api.Dtos.Responses;
using Aedis.App1.Api.Hypermedia;
using Aedis.App1.Api.Mappers;
using Aedis.Hosting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aedis.App1.Api;

/// <summary>
///     Host da API. Por herdar de <see cref="AedisApiHost" />, já sobe com health probes
///     (<c>/health</c>, <c>/health/live</c>, <c>/health/ready</c>), security headers, rate limiting,
///     ProblemDetails (RFC 9457), validação 422 e o portão de autenticação fail-closed. Aqui o host apenas
///     compõe as camadas (aplicação, infraestrutura), liga os controllers e os plugins de hipermídia
///     (<c>Aedis.Hypermedia.AspNetCore</c>) e de documentação (<c>Aedis.OpenApi</c> + <c>Aedis.OpenApi.Scalar</c>,
///     expostos só em Development).
/// </summary>
public sealed class ApiHost : AedisApiHost {
    /// <inheritdoc />
    protected override void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration) {
        services.AddAedisKeycloakAuth(configuration);
    }

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddControllers();
        services.AddAedisApiValidation(typeof(ApiHost).Assembly);

        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddAedisAuditContext();

        services.AddScoped<ProductMapper>();
        services.AddAedisHypermedia().Resource<ProductResponse, ProductLinks>();
        services.AddAedisOpenApi(configuration);
    }

    /// <inheritdoc />
    protected override void ConfigureMiddleware(WebApplication app) {
        app.MapControllers();

        if (app.Environment.IsDevelopment()) {
            app.MapAedisOpenApi();
            app.MapAedisScalar();
        }
    }
}
