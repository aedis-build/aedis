using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aedis.OpenApi;

/// <summary>
///     Documents the authorization of each operation from the policies the API itself uses, with nothing
///     extra to declare: <c>x-aedis-roles</c> (sorted roles required by <c>[Authorize(Roles)]</c> and by the
///     resolved policies) or <c>x-aedis-anonymous: true</c> for <c>[AllowAnonymous]</c>. Nothing is emitted
///     when the operation only requires authentication. Portals show the roles in the contract and agents
///     know which role a tool needs before calling it.
/// </summary>
public sealed class AuthorizationOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken) {
        var (anonymous, roles) = await OperationMetadata.AuthorizationAsync(context.Description, context.ApplicationServices);
        if (!anonymous && roles.Count == 0)
            return;

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        if (anonymous)
            operation.Extensions[OperationMetadata.Anonymous] = new JsonNodeExtension(JsonValue.Create(true));
        else
            operation.Extensions[OperationMetadata.Roles] = new JsonNodeExtension(new JsonArray(roles.Select(role => (JsonNode)role).ToArray()));
    }
}
