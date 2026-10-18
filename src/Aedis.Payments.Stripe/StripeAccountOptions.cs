namespace Aedis.Payments.Stripe;

/// <summary>Credenciais e ajustes de uma conta Stripe (<c>Payments:Stripe:Accounts:{nome}</c>).</summary>
public sealed class StripeAccountOptions
{
    /// <summary>Chave secreta (<c>sk_test_…</c>) ou restrita (<c>rk_test_…</c>) da conta.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Chave pública (<c>pk_test_…</c>), entregue ao front-end para coletar cartão.</summary>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>Segredo de assinatura do endpoint de webhook desta conta (<c>whsec_…</c>).</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    ///     Liga o Adaptive Pricing no Checkout da assinatura: o cliente estrangeiro vê e paga o preço na moeda dele,
    ///     recalculado pela Stripe a cada ciclo. Também precisa estar habilitado no Dashboard da conta.
    /// </summary>
    public bool AdaptivePricing { get; set; }
}
