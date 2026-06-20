using Aedis.Hosting.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Liga o endpoint do JSON OpenAPI e a UI do Swagger no prefixo configurado. Chamado pelo host apenas
///     quando o Swagger está habilitado (opt-in).
/// </summary>
public static class AedisSwaggerApplicationBuilderExtensions
{
    /// <summary>Expõe <c>UseSwagger</c> e a UI em <c>Swagger:RoutePrefix</c> conforme as opções vinculadas.</summary>
    public static IApplicationBuilder UseAedisSwagger(this IApplicationBuilder app, IConfiguration configuration) {
        var options = configuration.GetSection(Aedis.Hosting.AspNetCore.OpenApi.SwaggerOptions.SectionName).Get<Aedis.Hosting.AspNetCore.OpenApi.SwaggerOptions>()
                      ?? new Aedis.Hosting.AspNetCore.OpenApi.SwaggerOptions();

        app.UseSwagger();
        app.UseSwaggerUI(ui => {
            ui.SwaggerEndpoint($"/swagger/{options.DocumentName}/swagger.json", $"{options.Title ?? "API"} {options.Version}");
            ui.RoutePrefix = options.RoutePrefix;
        });

        return app;
    }
}
