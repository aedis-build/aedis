using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aedis.OpenApi;

/// <summary>
///     Fills the operation from the application's XML documentation of the action: <c>&lt;summary&gt;</c> as
///     summary, <c>&lt;remarks&gt;</c> as description and <c>&lt;param&gt;</c> as the parameter (and request body)
///     descriptions. Only empty values are filled, so <c>[EndpointSummary]</c>, <c>[EndpointDescription]</c> and the
///     <c>[Obsolete]</c> message always win. Requires <c>GenerateDocumentationFile</c> in the application project.
/// </summary>
public sealed class XmlCommentsOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken) {
        if (context.Description.ActionDescriptor is not ControllerActionDescriptor { MethodInfo: var action })
            return Task.CompletedTask;

        if (string.IsNullOrEmpty(operation.Summary))
            operation.Summary = XmlDocumentation.Summary(action);

        if (string.IsNullOrEmpty(operation.Description))
            operation.Description = XmlDocumentation.Remarks(action);

        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
            if (string.IsNullOrEmpty(parameter.Description) && parameter.Name is { } name)
                parameter.Description = XmlDocumentation.Param(action, name);

        var body = context.Description.ParameterDescriptions.FirstOrDefault(p => p.Source == BindingSource.Body);
        if (operation.RequestBody is OpenApiRequestBody requestBody && string.IsNullOrEmpty(requestBody.Description) && body is not null)
            requestBody.Description = XmlDocumentation.Param(action, body.Name);

        return Task.CompletedTask;
    }
}
