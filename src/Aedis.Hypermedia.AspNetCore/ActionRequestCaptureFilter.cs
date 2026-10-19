using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Aedis.Hypermedia.AspNetCore;

/// <summary>
///     Captura o request de consulta da action (o DTO já materializado pelo model binding) em
///     <see cref="HttpContext.Items" />, para a hipermídia ecoar os filtros nos <c>_templates</c> sem que o
///     controller precise repassá-lo. Elegível: um único argumento de tipo complexo vindo do corpo ou da query
///     string (inclusive por convenção, sem atributo); tipos por valor, <c>string</c>, coleções, uploads e
///     argumentos de rota/header/serviço são ignorados. Com mais de um candidato nada é capturado — adivinhar
///     seria pior do que deixar o controller informar explicitamente.
/// </summary>
public sealed class ActionRequestCaptureFilter : IActionFilter
{
    internal const string ItemKey = "Aedis.Hypermedia.ActionRequest";

    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context) {
        ArgumentNullException.ThrowIfNull(context);

        var request = ResolveRequest(context);
        if (request is not null) context.HttpContext.Items[ItemKey] = request;
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context) { }

    private static object? ResolveRequest(ActionExecutingContext context) {
        object? candidate = null;

        foreach (var argument in context.ActionArguments) {
            if (argument.Value is null) continue;
            if (!IsEligibleSource(context, argument.Key)) continue;
            if (!IsEligibleType(argument.Value.GetType())) continue;
            if (candidate is not null) return null;

            candidate = argument.Value;
        }

        return candidate;
    }

    private static bool IsEligibleSource(ActionExecutingContext context, string parameterName) {
        var parameter = context.ActionDescriptor.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.Ordinal));
        var source = parameter?.BindingInfo?.BindingSource;

        return source is null || source == BindingSource.Body || source == BindingSource.Query;
    }

    private static bool IsEligibleType(Type type) {
        if (type.IsValueType || type == typeof(string)) return false;
        if (typeof(IFormFile).IsAssignableFrom(type) || typeof(IFormFileCollection).IsAssignableFrom(type)) return false;

        return !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }
}
