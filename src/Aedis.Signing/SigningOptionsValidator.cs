using Aedis.Signing.Abstractions;
using Microsoft.Extensions.Options;

namespace Aedis.Signing;

/// <summary>Regras cruzadas das opções de assinatura que DataAnnotations não expressam; fail-closed na subida.</summary>
internal sealed class SigningOptionsValidator : IValidateOptions<SigningOptions>
{
    public ValidateOptionsResult Validate(string? name, SigningOptions options) {
        if (!options.Enabled) return ValidateOptionsResult.Success;

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Provider))
            errors.Add("Signing:Provider é obrigatório.");
        else if (string.Equals(options.Provider, SigningKeyProviders.InProcess, StringComparison.OrdinalIgnoreCase)
                 && string.IsNullOrWhiteSpace(options.InProcess.PrivateKeyPem) && !options.InProcess.AllowEphemeralKey)
            errors.Add("Signing:InProcess:PrivateKeyPem ou Signing:InProcess:AllowEphemeralKey=true é obrigatório com Provider=InProcess.");

        if (!options.Jwks.Path.StartsWith('/'))
            errors.Add("Signing:Jwks:Path deve começar com '/'.");

        var components = options.Http.Components;
        if (components.Count == 0 || !components.Contains("@method") || !components.Contains("content-digest"))
            errors.Add("Signing:Http:Components deve conter ao menos '@method' e 'content-digest'.");
        if (components.Count != components.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            errors.Add("Signing:Http:Components tem componentes repetidos.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
