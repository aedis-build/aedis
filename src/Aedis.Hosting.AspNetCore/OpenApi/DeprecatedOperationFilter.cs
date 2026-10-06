using System.Reflection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Aedis.Hosting.AspNetCore.OpenApi;

/// <summary>
///     Marca como <c>deprecated</c> no documento OpenAPI as operações cujas actions (ou o controller inteiro)
///     estão anotadas com <see cref="ObsoleteAttribute" />. O gerador não deriva isso do atributo sozinho; quando
///     o atributo traz mensagem, ela passa a ser a descrição da operação.
/// </summary>
public sealed class DeprecatedOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context) {
        var obsolete = context.MethodInfo?.GetCustomAttribute<ObsoleteAttribute>(inherit: false)
                       ?? context.MethodInfo?.DeclaringType?.GetCustomAttribute<ObsoleteAttribute>(inherit: false);

        if (obsolete is null) return;

        operation.Deprecated = true;
        if (!string.IsNullOrWhiteSpace(obsolete.Message))
            operation.Description = obsolete.Message;
    }
}
