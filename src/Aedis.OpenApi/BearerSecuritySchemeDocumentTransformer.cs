using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aedis.OpenApi;

/// <summary>
///     Declares the HTTP <c>Bearer</c> (JWT) security scheme in <c>components.securitySchemes</c> and requires
///     it document-wide, so the UI can authorize requests. Added when
///     <c>AedisOpenApiOptions.IncludeBearerSecurity</c> is <c>true</c>.
/// </summary>
public sealed class BearerSecuritySchemeDocumentTransformer : IOpenApiDocumentTransformer
{
    /// <summary>Identifier of the Bearer scheme in the document.</summary>
    public const string SchemeId = "Bearer";

    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken) {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT access token: Authorization: Bearer {token}"
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement {
            [new OpenApiSecuritySchemeReference(SchemeId, document)] = []
        });

        return Task.CompletedTask;
    }
}
