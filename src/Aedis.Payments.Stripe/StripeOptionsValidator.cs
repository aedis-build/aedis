using Microsoft.Extensions.Options;

namespace Aedis.Payments.Stripe;

/// <summary>
///     Valida <see cref="StripeOptions" /> na subida: ao menos uma conta, as três chaves com o prefixo certo, sem misturar
///     teste e live, e live só com <see cref="StripeOptions.AllowLiveMode" />. As mensagens citam o nome da conta, nunca
///     o valor da chave.
/// </summary>
internal sealed class StripeOptionsValidator : IValidateOptions<StripeOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, StripeOptions options) {
        var failures = new List<string>();
        if (options.Accounts.Count == 0) failures.Add($"{StripeOptions.SectionName}:Accounts precisa de ao menos uma conta.");

        foreach (var (account, keys) in options.Accounts) {
            var prefix = $"{StripeOptions.SectionName}:Accounts:{account}";
            if (!HasPrefix(keys.SecretKey, "sk_", "rk_")) failures.Add($"{prefix}:SecretKey deve ser uma chave secreta (sk_) ou restrita (rk_).");
            if (!HasPrefix(keys.PublishableKey, "pk_")) failures.Add($"{prefix}:PublishableKey deve ser uma chave pública (pk_).");
            if (!HasPrefix(keys.WebhookSecret, "whsec_")) failures.Add($"{prefix}:WebhookSecret deve ser o segredo do endpoint (whsec_).");

            var secretLive = IsLive(keys.SecretKey);
            if (secretLive != IsLive(keys.PublishableKey)) failures.Add($"{prefix} mistura chaves de teste e live.");
            if ((secretLive || IsLive(keys.PublishableKey)) && !options.AllowLiveMode)
                failures.Add($"{prefix} usa chave live; habilite {StripeOptions.SectionName}:AllowLiveMode só no ambiente de produção.");
        }

        if (!string.IsNullOrWhiteSpace(options.ApiBase) && !Uri.TryCreate(options.ApiBase, UriKind.Absolute, out _))
            failures.Add($"{StripeOptions.SectionName}:ApiBase deve ser uma URL absoluta.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool HasPrefix(string? key, params string[] prefixes) =>
        !string.IsNullOrWhiteSpace(key) && prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));

    private static bool IsLive(string? key) => key?.Contains("_live_", StringComparison.Ordinal) == true;
}
