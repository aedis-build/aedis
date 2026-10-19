using Aedis.OpenApi.Abstractions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aedis.OpenApi;

/// <summary>
///     Documents the standard ProblemDetails errors (401, 403, 404, 409, 422, 500) on the operations whose
///     action or controller carries <see cref="ProducesStandardErrorsAttribute" />, without overriding responses
///     the action already declares.
/// </summary>
public sealed class StandardErrorsOperationTransformer : IOpenApiOperationTransformer
{
    internal static readonly (string Code, string Description)[] StandardErrors = [
        ("401", "Unauthorized"),
        ("403", "Forbidden"),
        ("404", "Not Found"),
        ("409", "Conflict"),
        ("422", "Unprocessable Entity"),
        ("500", "Internal Server Error")
    ];

    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken) {
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<ProducesStandardErrorsAttribute>().Any())
            return Task.CompletedTask;

        operation.Responses ??= new OpenApiResponses();
        foreach (var (code, description) in StandardErrors)
            operation.Responses.TryAdd(code, new OpenApiResponse { Description = description });

        return Task.CompletedTask;
    }
}
