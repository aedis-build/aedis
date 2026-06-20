using System.Reflection;
using Aedis.Hosting.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registra a geração do documento OpenAPI/Swagger do Aedis a partir da seção <c>Swagger</c>. Chamado
///     pelo host apenas quando o Swagger está habilitado (opt-in). Inclui o esquema de segurança Bearer e os
///     comentários XML do assembly de entrada para uma documentação rica.
/// </summary>
public static class AedisSwaggerServiceExtensions
{
    /// <summary>
    ///     Configura <c>AddSwaggerGen</c> com o documento, o esquema Bearer (quando habilitado) e os XML
    ///     comments do assembly de entrada. Use <paramref name="configure" /> para ajustes adicionais.
    /// </summary>
    public static IServiceCollection AddAedisSwagger(
        this IServiceCollection services,
        IConfiguration configuration,
        string? defaultTitle = null,
        Action<SwaggerGenOptions>? configure = null) {
        var options = configuration.GetSection(SwaggerOptions.SectionName).Get<SwaggerOptions>() ?? new SwaggerOptions();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(swagger => {
            swagger.SwaggerDoc(options.DocumentName, new OpenApiInfo {
                Title = options.Title ?? defaultTitle ?? "API",
                Version = options.Version,
                Description = options.Description
            });

            if (options.IncludeBearerSecurity)
                AddBearerSecurity(swagger);

            IncludeEntryAssemblyXmlComments(swagger);
            configure?.Invoke(swagger);
        });

        return services;
    }

    private static void AddBearerSecurity(SwaggerGenOptions swagger) {
        swagger.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Informe o token JWT no formato: Bearer {token}"
        });

        swagger.AddSecurityRequirement(document => new OpenApiSecurityRequirement {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
        });
    }

    private static void IncludeEntryAssemblyXmlComments(SwaggerGenOptions swagger) {
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is null)
            return;

        var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{entryAssembly.GetName().Name}.xml");
        if (File.Exists(xmlPath))
            swagger.IncludeXmlComments(xmlPath);
    }
}
