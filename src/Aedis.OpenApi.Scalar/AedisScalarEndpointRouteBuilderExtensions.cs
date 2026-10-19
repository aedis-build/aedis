using System.Reflection;
using Aedis.OpenApi.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Maps the Scalar API reference at <c>/{RoutePrefix}</c>, reading the document the <c>Aedis.OpenApi</c>
///     plugin serves at <c>/{RoutePrefix}/{documentName}.json</c>. Opt-in like the document: call it only
///     where the UI should be exposed (the template guards it with <c>IsDevelopment()</c>).
/// </summary>
public static class AedisScalarEndpointRouteBuilderExtensions
{
    /// <summary>Maps the Scalar UI with the title and route pattern from <see cref="AedisOpenApiOptions" />.</summary>
    /// <param name="app">Endpoint route builder of the application.</param>
    /// <param name="configure">Optional hook over the Scalar options, applied after the Aedis defaults.</param>
    public static IEndpointConventionBuilder MapAedisScalar(this IEndpointRouteBuilder app, Action<ScalarOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ServiceProvider.GetRequiredService<IOptions<AedisOpenApiOptions>>().Value;
        return app.MapScalarApiReference($"/{options.RoutePrefix}", scalar => {
            scalar.WithTitle(options.Title ?? Assembly.GetEntryAssembly()?.GetName().Name ?? "API")
                .WithOpenApiRoutePattern($"/{options.RoutePrefix}/{{documentName}}.json");
            configure?.Invoke(scalar);
        });
    }
}
