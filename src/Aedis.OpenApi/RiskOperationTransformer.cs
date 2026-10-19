using Aedis.OpenApi.Abstractions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aedis.OpenApi;

/// <summary>
///     Emits <c>x-aedis-risk: { "level": "HIGH", "reason": "..." }</c> on the operations annotated with
///     <see cref="OperationRiskAttribute" /> (action over controller). Writes without the annotation carry
///     no extension and the executor applies its own default.
/// </summary>
public sealed class RiskOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken) {
        if (OperationMetadata.RiskOf(context.Description) is { } risk) {
            operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            operation.Extensions[OperationMetadata.Risk] = new JsonNodeExtension(risk);
        }

        return Task.CompletedTask;
    }
}
