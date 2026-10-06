using Microsoft.Extensions.Options;

namespace Aedis.Security.Web.Options;

/// <summary>
///     Validação fail-closed das opções de segurança HTTP, executada na subida do host: CORS ligado exige
///     origens explícitas e não pode combinar credenciais com a origem <c>*</c>.
/// </summary>
internal sealed class WebSecurityOptionsValidator : IValidateOptions<WebSecurityOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, WebSecurityOptions options) {
        var cors = options.Cors;
        if (!cors.Enabled)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        if (cors.AllowedOrigins.All(string.IsNullOrWhiteSpace))
            failures.Add("Security:Cors:AllowedOrigins deve listar ao menos uma origem quando o CORS está habilitado; não há fallback para qualquer origem.");

        if (cors.AllowCredentials && cors.AllowsAnyOrigin)
            failures.Add("Security:Cors não pode combinar AllowCredentials com a origem '*': o navegador rejeita e a combinação exporia credenciais a qualquer site.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
