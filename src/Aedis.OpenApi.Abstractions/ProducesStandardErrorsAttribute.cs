namespace Aedis.OpenApi.Abstractions;

/// <summary>
///     Declares that the action (or every action of the controller) answers with the Aedis standard errors as
///     ProblemDetails — 401, 403, 404, 409, 422 and 500 — without repeating a <c>[ProducesResponseType]</c> per
///     code. The <c>Aedis.OpenApi</c> plugin materializes those responses in the document.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class ProducesStandardErrorsAttribute : Attribute
{
}
