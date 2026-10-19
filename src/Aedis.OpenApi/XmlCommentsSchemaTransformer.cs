using System.Reflection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aedis.OpenApi;

/// <summary>
///     Fills schema descriptions from the application's XML documentation: the type's <c>&lt;summary&gt;</c> for
///     DTO schemas and the property's <c>&lt;summary&gt;</c> (or, for positional records, the type's
///     <c>&lt;param&gt;</c>) for property schemas. Existing descriptions (e.g. <c>[Description]</c>) are kept.
/// </summary>
public sealed class XmlCommentsSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken) {
        if (!string.IsNullOrEmpty(schema.Description))
            return Task.CompletedTask;

        schema.Description = context.JsonPropertyInfo?.AttributeProvider is PropertyInfo property
            ? XmlDocumentation.Summary(property) ?? XmlDocumentation.Param(property.DeclaringType!, property.Name)
            : context.JsonPropertyInfo is null ? XmlDocumentation.Summary(context.JsonTypeInfo.Type) : null;

        return Task.CompletedTask;
    }
}
