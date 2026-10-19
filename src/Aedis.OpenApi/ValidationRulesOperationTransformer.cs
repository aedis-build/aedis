using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aedis.OpenApi;

/// <summary>
///     Emits <c>x-aedis-rules</c> with the FluentValidation rules of the body and of the complex query/route
///     objects of the operation (see <see cref="ValidationRules" />). No validator, no extension. Validation
///     still runs in the API; the extension only publishes the rules so portals and agents know the 422
///     contract before calling, instead of reimplementing it.
/// </summary>
public sealed class ValidationRulesOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken) {
        if (ValidationRules.Of(context.Description, context.ApplicationServices) is { } rules) {
            operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            operation.Extensions[OperationMetadata.Rules] = new JsonNodeExtension(rules);
        }

        return Task.CompletedTask;
    }
}
