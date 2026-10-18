using Stripe;

namespace Aedis.Payments.Stripe;

/// <summary>Uma conta Stripe configurada: nome, cliente da API e chaves.</summary>
internal sealed record StripeAccount(string Name, StripeClient Client, StripeAccountOptions Options);
