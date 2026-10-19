using System.Text.Json.Nodes;
using Aedis.Hypermedia.Abstractions;
using Aedis.OpenApi.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aedis.OpenApi;

/// <summary>
///     Exposes the <c>QUERY</c> actions in the document. OpenAPI 3.1 has no <c>query</c> field in the Path
///     Item (it arrives in 3.2 as <c>additionalOperations</c>), so the generator skips those actions and this
///     transformer synthesizes them under the <c>x-oai-additionalOperations</c> extension — the shape tools
///     like Scalar already read. Body and responses point (<c>$ref</c>) to the component schemas the other
///     operations produced, when present; <c>[EndpointSummary]</c>/<c>[EndpointDescription]</c>,
///     <c>[Obsolete]</c> and <c>[ProducesStandardErrors]</c> are honored.
/// </summary>
public sealed class QueryOperationsDocumentTransformer : IOpenApiDocumentTransformer
{
    /// <summary>Name of the Path Item extension that carries the additional operations.</summary>
    public const string ExtensionName = "x-oai-additionalOperations";

    /// <summary>True for API descriptions of the <c>QUERY</c> method.</summary>
    public static bool IsQuery(ApiDescription api) =>
        string.Equals(api.HttpMethod, HalFormsTemplate.QueryMethod, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken) {
        var queryApis = context.DescriptionGroups.SelectMany(group => group.Items).Where(IsQuery).ToList();
        if (queryApis.Count == 0)
            return Task.CompletedTask;

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

            additional[HalFormsTemplate.QueryMethod] = BuildOperation(api, document);
            pathItem.Extensions[ExtensionName] = new JsonNodeExtension(additional);
        }

        return Task.CompletedTask;
    }

    private static JsonObject BuildOperation(ApiDescription api, OpenApiDocument document) {
        var operation = new JsonObject();
        var metadata = api.ActionDescriptor.EndpointMetadata;

        if (api.ActionDescriptor is ControllerActionDescriptor descriptor)
            operation["tags"] = new JsonArray(descriptor.ControllerName);

        if (metadata.OfType<EndpointSummaryAttribute>().FirstOrDefault()?.Summary is { } summary)
            operation["summary"] = summary;

        if (metadata.OfType<EndpointDescriptionAttribute>().FirstOrDefault()?.Description is { } description)
            operation["description"] = description;

        if (metadata.OfType<ObsoleteAttribute>().Any())
            operation["deprecated"] = true;

        var body = api.ParameterDescriptions.FirstOrDefault(p => p.Source == BindingSource.Body);
        if (body?.Type is { } bodyType && SchemaReference(bodyType, document) is { } bodySchema)
            operation["requestBody"] = new JsonObject {
                ["required"] = true,
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = bodySchema } }
            };

        var responses = new JsonObject();
        foreach (var responseType in api.SupportedResponseTypes) {
            var response = new JsonObject { ["description"] = ReasonPhrase(responseType.StatusCode) };
            if (responseType.Type is { } type && type != typeof(void) && SchemaReference(type, document) is { } schema)
                response["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = schema } };

            responses[responseType.IsDefaultResponse ? "default" : responseType.StatusCode.ToString()] = response;
        }

        if (metadata.OfType<ProducesStandardErrorsAttribute>().Any())
            foreach (var (code, reason) in StandardErrorsOperationTransformer.StandardErrors)
                responses[code] ??= new JsonObject { ["description"] = reason };

        if (responses.Count == 0)
            responses["200"] = new JsonObject { ["description"] = "OK" };

        operation["responses"] = responses;
        return operation;
    }

    private static JsonObject? SchemaReference(Type type, OpenApiDocument document) {
        var id = SchemaReferenceIds.For(type);
        return document.Components?.Schemas?.ContainsKey(id) == true
            ? new JsonObject { ["$ref"] = $"#/components/schemas/{id}" }
            : null;
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch {
        0 or 200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        _ => $"HTTP {statusCode}"
    };
}
