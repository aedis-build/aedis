using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aedis.OpenApi;

/// <summary>
///     Marks as <c>deprecated</c> the operations whose action (or whole controller) is annotated with
///     <see cref="ObsoleteAttribute" />; the native generator does not derive it from the attribute. When the
///     attribute carries a message, it becomes the operation description.
/// </summary>
public sealed class ObsoleteOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken) {
        var obsolete = context.Description.ActionDescriptor.EndpointMetadata.OfType<ObsoleteAttribute>().FirstOrDefault();
        if (obsolete is null)
            return Task.CompletedTask;

        operation.Deprecated = true;
        if (!string.IsNullOrWhiteSpace(obsolete.Message))
            operation.Description = obsolete.Message;

        return Task.CompletedTask;
    }
}
