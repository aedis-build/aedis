namespace Aedis.Payments.Stripe.Tests;

/// <summary>Configuração de duas contas de teste (Brasil e global), como numa Organization da Stripe.</summary>
internal static class TestAccounts
{
    public const string BrWebhookSecret = "whsec_br_teste";
    public const string GlobalWebhookSecret = "whsec_global_teste";

    public static StripeOptions Options() => new() {
        Accounts = new Dictionary<string, StripeAccountOptions>(StringComparer.OrdinalIgnoreCase) {
            ["br"] = new() { SecretKey = "sk_test_br", PublishableKey = "pk_test_br", WebhookSecret = BrWebhookSecret },
            ["global"] = new() {
                SecretKey = "sk_test_global", PublishableKey = "pk_test_global", WebhookSecret = GlobalWebhookSecret,
                AdaptivePricing = true,
            },
        },
    };
}
