using System.Reflection;
using Aedis.OpenApi;
using Aedis.OpenApi.Abstractions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registers the Aedis OpenAPI document over the native ASP.NET Core generator, bound to the
///     <see cref="AedisOpenApiOptions.SectionName" /> section: OpenAPI 3.1, document info, the Bearer scheme
///     (when enabled), <c>[Obsolete]</c> as <c>deprecated</c>, the <c>[ProducesStandardErrors]</c> responses,
///     the <c>QUERY</c> operations under <c>x-oai-additionalOperations</c>, the HAL schema ids and the
///     <c>x-aedis-roles</c>/<c>x-aedis-anonymous</c>/<c>x-aedis-risk</c>/<c>x-aedis-rules</c> extensions that
///     portals and agents read instead of reimplementing authorization, risk and 422 rules. The application's XML
///     documentation comments (actions, parameters, DTOs) fill summaries and descriptions left empty by attributes.
/// </summary>
public static class AedisOpenApiServiceCollectionExtensions
{
    /// <summary>
    ///     Binds <see cref="AedisOpenApiOptions" /> from <paramref name="configuration" /> and registers the
    ///     document with the Aedis transformers. Use <paramref name="configure" /> for further adjustments
    ///     (extra transformers, schema tweaks).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration (section <c>OpenApi</c>).</param>
    /// <param name="configure">Optional hook over the generator options, applied after the Aedis defaults.</param>
    public static IServiceCollection AddAedisOpenApi(this IServiceCollection services, IConfiguration configuration, Action<OpenApiOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AedisOpenApiOptions>()
            .Bind(configuration.GetSection(AedisOpenApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(AedisOpenApiOptions.SectionName).Get<AedisOpenApiOptions>() ?? new AedisOpenApiOptions();

        services.AddOpenApi(options.DocumentName, openApi => {
            openApi.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;

            var includeByGroup = openApi.ShouldInclude;
            openApi.ShouldInclude = api => !QueryOperationsDocumentTransformer.IsQuery(api) && includeByGroup(api);

            openApi.AddDocumentTransformer((document, _, _) => {
                document.Info = new OpenApiInfo {
                    Title = options.Title ?? Assembly.GetEntryAssembly()?.GetName().Name ?? "API",
                    Version = options.Version,
                    Description = options.Description
                };
                return Task.CompletedTask;
            });

            openApi.AddOperationTransformer<ObsoleteOperationTransformer>();
            openApi.AddOperationTransformer<StandardErrorsOperationTransformer>();
            openApi.AddOperationTransformer<AuthorizationOperationTransformer>();
            openApi.AddOperationTransformer<RiskOperationTransformer>();
            openApi.AddOperationTransformer<ValidationRulesOperationTransformer>();
            openApi.AddOperationTransformer<XmlCommentsOperationTransformer>();
            openApi.AddSchemaTransformer<XmlCommentsSchemaTransformer>();
            openApi.AddDocumentTransformer<QueryOperationsDocumentTransformer>();

            if (options.IncludeBearerSecurity)
                openApi.AddDocumentTransformer<BearerSecuritySchemeDocumentTransformer>();

            openApi.CreateSchemaReferenceId = typeInfo => {
                var id = OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo);
                return id is not null && typeInfo.Type.IsGenericType ? SchemaReferenceIds.For(typeInfo.Type) : id;
            };

            configure?.Invoke(openApi);
        });

        return services;
    }
}
