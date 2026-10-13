using System.Text.Json.Nodes;
using Aedis.Hosting.AspNetCore.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Aedis.Hosting.AspNetCore.OpenApi;

/// <summary>
///     Expõe as actions <c>QUERY</c> no documento OpenAPI. O Swashbuckle só conhece os verbos do Path Item
///     (OpenAPI 3.0/3.1) e lançaria num verbo desconhecido, então o host exclui as actions QUERY da geração
///     normal (<c>DocInclusionPredicate</c>) e este filtro as reintroduz sob a extensão
///     <c>x-oai-additionalOperations</c> do Path Item — o formato que o OpenAPI 3.2 torna nativo
///     (<c>additionalOperations</c>) e que ferramentas como o Scalar já leem. Corpo e respostas usam os mesmos
///     schemas gerados para as demais operações; <c>[EndpointSummary]</c>/<c>[EndpointDescription]</c>,
///     <c>[Obsolete]</c> e <c>[ProducesStandardErrors]</c> são respeitados.
/// </summary>
public sealed class QueryOperationsDocumentFilter : IDocumentFilter
{
    private readonly IApiDescriptionGroupCollectionProvider _apiDescriptions;

    /// <summary>
    ///     Cria o filtro sobre o catálogo completo de actions: o <c>DocInclusionPredicate</c> já retirou as
    ///     QUERY do contexto do Swashbuckle, então elas são lidas direto do ApiExplorer.
    /// </summary>
    public QueryOperationsDocumentFilter(IApiDescriptionGroupCollectionProvider apiDescriptions) {
        _apiDescriptions = apiDescriptions ?? throw new ArgumentNullException(nameof(apiDescriptions));
    }

    /// <summary>Nome da extensão do Path Item que carrega as operações adicionais.</summary>
    public const string ExtensionName = "x-oai-additionalOperations";

    /// <summary>Verdadeiro para descrições de API do método <c>QUERY</c>.</summary>
    public static bool IsQuery(ApiDescription api) =>
        string.Equals(api.HttpMethod, HttpQueryAttribute.Method, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Apply(OpenApiDocument document, DocumentFilterContext context) {
        var queryApis = _apiDescriptions.ApiDescriptionGroups.Items.SelectMany(group => group.Items).Where(IsQuery).ToList();
        if (queryApis.Count == 0)
            return;

        document.Paths ??= new OpenApiPaths();

        foreach (var api in queryApis) {
            var path = "/" + (api.RelativePath ?? string.Empty).TrimStart('/');
            if (!document.Paths.TryGetValue(path, out var existing) || existing is not OpenApiPathItem pathItem) {
                pathItem = new OpenApiPathItem();
                document.Paths[path] = pathItem;
            }

            pathItem.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            var additional = pathItem.Extensions.TryGetValue(ExtensionName, out var current) && current is JsonNodeExtension { Node: JsonObject node }
                ? node
                : new JsonObject();

            additional[HttpQueryAttribute.Method] = Serialize(BuildOperation(api, document, context));
            pathItem.Extensions[ExtensionName] = new JsonNodeExtension(additional);
        }
    }

    private static OpenApiOperation BuildOperation(ApiDescription api, OpenApiDocument document, DocumentFilterContext context) {
        var operation = new OpenApiOperation { Responses = new OpenApiResponses() };

        if (api.ActionDescriptor is ControllerActionDescriptor descriptor) {
            operation.Tags = new HashSet<OpenApiTagReference> { new(descriptor.ControllerName, document) };
            operation.Summary = descriptor.MethodInfo.GetCustomAttributes(true).OfType<EndpointSummaryAttribute>().FirstOrDefault()?.Summary;
            operation.Description = descriptor.MethodInfo.GetCustomAttributes(true).OfType<EndpointDescriptionAttribute>().FirstOrDefault()?.Description;
            operation.Deprecated = descriptor.MethodInfo.GetCustomAttributes(typeof(ObsoleteAttribute), true).Length > 0
                                   || descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(ObsoleteAttribute), true).Length > 0;

            var standardErrors = descriptor.MethodInfo.GetCustomAttributes(typeof(ProducesStandardErrorsAttribute), true).Length > 0
                                 || descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(ProducesStandardErrorsAttribute), true).Length > 0;
            if (standardErrors)
                foreach (var (code, description) in StandardErrorsOperationFilter.StandardErrors)
                    operation.Responses[code] = new OpenApiResponse { Description = description };
        }

        var body = api.ParameterDescriptions.FirstOrDefault(p => p.Source == BindingSource.Body);
        if (body?.Type is { } bodyType)
            operation.RequestBody = new OpenApiRequestBody {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType> {
                    ["application/json"] = new() { Schema = context.SchemaGenerator.GenerateSchema(bodyType, context.SchemaRepository) }
                }
            };

        foreach (var responseType in api.SupportedResponseTypes) {
            var code = responseType.IsDefaultResponse ? "default" : responseType.StatusCode.ToString();
            var response = new OpenApiResponse { Description = ReasonPhrase(responseType.StatusCode) };
            if (responseType.Type is { } type && type != typeof(void))
                response.Content = new Dictionary<string, OpenApiMediaType> {
                    ["application/json"] = new() { Schema = context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository) }
                };
            operation.Responses[code] = response;
        }

        if (operation.Responses.Count == 0)
            operation.Responses["200"] = new OpenApiResponse { Description = "OK" };

        return operation;
    }

    private static JsonNode Serialize(OpenApiOperation operation) {
        using var text = new StringWriter();
        var writer = new OpenApiJsonWriter(text);
        operation.SerializeAsV3(writer);
        text.Flush();
        return JsonNode.Parse(text.ToString()) ?? new JsonObject();
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        0 => "OK",
        _ => $"HTTP {statusCode}"
    };
}
