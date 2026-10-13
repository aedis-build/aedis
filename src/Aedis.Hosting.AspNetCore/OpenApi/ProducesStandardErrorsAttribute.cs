using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Aedis.Hosting.AspNetCore.OpenApi;

/// <summary>
///     Declara que a action (ou todas as actions do controller) responde com os erros padrão do Aedis em
///     ProblemDetails — 401, 403, 404, 409, 422 e 500 — sem repetir um <c>[ProducesResponseType]</c> por código.
///     O <see cref="StandardErrorsOperationFilter" /> acrescenta essas respostas ao documento OpenAPI.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class ProducesStandardErrorsAttribute : Attribute
{
}

/// <summary>Filtro Swashbuckle que materializa as respostas de <see cref="ProducesStandardErrorsAttribute" />.</summary>
public sealed class StandardErrorsOperationFilter : IOperationFilter
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
    public void Apply(OpenApiOperation operation, OperationFilterContext context) {
        var method = context.MethodInfo;
        var declared = method.GetCustomAttributes(typeof(ProducesStandardErrorsAttribute), true).Length > 0
                       || method.DeclaringType?.GetCustomAttributes(typeof(ProducesStandardErrorsAttribute), true).Length > 0;
        if (!declared)
            return;

        operation.Responses ??= new OpenApiResponses();
        foreach (var (code, description) in StandardErrors)
            if (!operation.Responses.ContainsKey(code))
                operation.Responses[code] = new OpenApiResponse { Description = description };
    }
}
