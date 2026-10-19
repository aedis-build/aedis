using Aedis.OpenApi.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Maps the OpenAPI JSON endpoint at <c>/{RoutePrefix}/{documentName}.json</c> using the bound
///     <see cref="AedisOpenApiOptions" />. Documentation is opt-in: call it only where it should be exposed
///     (the template guards it with <c>IsDevelopment()</c>).
/// </summary>
public static class AedisOpenApiEndpointRouteBuilderExtensions
{
    /// <summary>Maps the OpenAPI document route (e.g. <c>/openapi/v1.json</c>).</summary>
    /// <param name="app">Endpoint route builder of the application.</param>
    public static IEndpointConventionBuilder MapAedisOpenApi(this IEndpointRouteBuilder app) {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ServiceProvider.GetRequiredService<IOptions<AedisOpenApiOptions>>().Value;
        return app.MapOpenApi($"/{options.RoutePrefix}/{{documentName}}.json");
    }
}
