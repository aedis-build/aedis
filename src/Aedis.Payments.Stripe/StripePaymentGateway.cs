using Aedis.Payments.Abstractions;
using Stripe;
using Stripe.Checkout;

namespace Aedis.Payments.Stripe;

/// <summary>
///     <see cref="IPaymentGateway" /> sobre a Stripe. Assinatura nasce pelo Checkout hospedado (coleta o cartão, cobra a
///     primeira mensalidade e é onde o Adaptive Pricing atua); troca de plano usa <c>proration_behavior=always_invoice</c>
///     para cobrar a diferença na hora; cobrança avulsa é uma <b>fatura</b> com itens, finalizada e paga off-session —
///     não um PaymentIntent solto — para ter linhas, PDF e os mesmos eventos <c>invoice.paid</c>/<c>invoice.payment_failed</c>
///     da mensalidade, e poder ser retentada com <c>invoices/pay</c>.
/// </summary>
internal sealed class StripePaymentGateway(StripeAccounts accounts) : IPaymentGateway
{
    /// <summary>Metadado com o tenant dono do cliente.</summary>
    public const string TenantMetadata = "aedis_tenant_id";

    /// <summary>Metadado com a chave de idempotência da cobrança avulsa.</summary>
    public const string IdempotencyMetadata = "aedis_idempotency_key";

    /// <inheritdoc />
    public async Task<PaymentRef> CreateCustomerAsync(NewCustomer customer, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentException.ThrowIfNullOrWhiteSpace(customer.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customer.Email);
        var account = accounts.Get(customer.Account);

        var created = await account.Client.V1.Customers.CreateAsync(new CustomerCreateOptions {
            Email = customer.Email,
            Name = customer.Name,
            Metadata = new Dictionary<string, string> { [TenantMetadata] = customer.TenantId },
        }, new RequestOptions { IdempotencyKey = $"aedis:customer:{customer.TenantId}" }, cancellationToken);

        return new PaymentRef(account.Name, created.Id);
    }

    /// <inheritdoc />
    public async Task<Uri> CreateSubscriptionCheckoutAsync(SubscriptionCheckout checkout, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(checkout);
        var account = accounts.Get(checkout.Customer.Account);

        var session = await account.Client.V1.Checkout.Sessions.CreateAsync(new SessionCreateOptions {
            Mode = "subscription",
            Customer = checkout.Customer.Id,
            LineItems = [StripeMapper.ToCheckoutLine(checkout.Plan)],
            SuccessUrl = checkout.SuccessUrl.AbsoluteUri,
            CancelUrl = checkout.CancelUrl.AbsoluteUri,
            AdaptivePricing = account.Options.AdaptivePricing ? new SessionAdaptivePricingOptions { Enabled = true } : null,
        }, cancellationToken: cancellationToken);

        return new Uri(session.Url);
    }

    /// <inheritdoc />
    public async Task<PaymentSubscription> GetSubscriptionAsync(PaymentRef subscription, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(subscription);
        var account = accounts.Get(subscription.Account);
        var current = await account.Client.V1.Subscriptions.GetAsync(subscription.Id, cancellationToken: cancellationToken);
        return StripeMapper.ToSubscription(account.Name, current);
    }

    /// <inheritdoc />
    public async Task<PaymentSubscription> ChangePlanAsync(PaymentRef subscription, PlanPrice plan, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(plan);
        var account = accounts.Get(subscription.Account);
        var current = await account.Client.V1.Subscriptions.GetAsync(subscription.Id, cancellationToken: cancellationToken);
        var items = current.Items?.Data ?? [];
        if (items.Count != 1)
            throw new InvalidOperationException($"Assinatura {subscription.Id} tem {items.Count} itens; a troca de plano espera exatamente um.");

        var updated = await account.Client.V1.Subscriptions.UpdateAsync(subscription.Id, new SubscriptionUpdateOptions {
            Items = [StripeMapper.ToSubscriptionItem(items[0].Id, plan)],
            ProrationBehavior = "always_invoice",
            CancelAtPeriodEnd = false,
        }, cancellationToken: cancellationToken);

        return StripeMapper.ToSubscription(account.Name, updated);
    }

    /// <inheritdoc />
    public async Task<PaymentSubscription> CancelAtPeriodEndAsync(PaymentRef subscription, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(subscription);
        var account = accounts.Get(subscription.Account);
        var updated = await account.Client.V1.Subscriptions.UpdateAsync(subscription.Id,
            new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }, cancellationToken: cancellationToken);
        return StripeMapper.ToSubscription(account.Name, updated);
    }

    /// <inheritdoc />
    public async Task<CardSetup> CreateCardSetupAsync(PaymentRef customer, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(customer);
        var account = accounts.Get(customer.Account);
        var intent = await account.Client.V1.SetupIntents.CreateAsync(new SetupIntentCreateOptions {
            Customer = customer.Id,
            Usage = "off_session",
        }, cancellationToken: cancellationToken);
        return new CardSetup(intent.ClientSecret, account.Options.PublishableKey);
    }

    /// <inheritdoc />
    public async Task<PaymentCharge> ChargeAsync(OneOffCharge charge, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(charge);
        ArgumentException.ThrowIfNullOrWhiteSpace(charge.IdempotencyKey);
        if (charge.Lines is not { Count: > 0 }) throw new ArgumentException("A cobrança precisa de ao menos uma linha.", nameof(charge));
        if (charge.Lines.Any(l => l.Amount <= 0 || string.IsNullOrWhiteSpace(l.Description)))
            throw new ArgumentException("Toda linha precisa de descrição e valor positivo.", nameof(charge));

        var account = accounts.Get(charge.Customer.Account);
        var currency = StripeMapper.ToStripeCurrency(charge.Currency);
        var key = charge.IdempotencyKey;

        var invoice = await account.Client.V1.Invoices.CreateAsync(new InvoiceCreateOptions {
            Customer = charge.Customer.Id,
            Currency = currency,
            CollectionMethod = "charge_automatically",
            AutoAdvance = false,
            PendingInvoiceItemsBehavior = "exclude",
            Description = charge.Description,
            Metadata = new Dictionary<string, string> { [IdempotencyMetadata] = key },
        }, Idempotent(key, "invoice"), cancellationToken);

        for (var i = 0; i < charge.Lines.Count; i++) {
            await account.Client.V1.InvoiceItems.CreateAsync(new InvoiceItemCreateOptions {
                Customer = charge.Customer.Id,
                Invoice = invoice.Id,
                Currency = currency,
                Amount = charge.Lines[i].Amount,
                Description = charge.Lines[i].Description,
            }, Idempotent(key, $"item:{i}"), cancellationToken);
        }

        await account.Client.V1.Invoices.FinalizeInvoiceAsync(invoice.Id, new InvoiceFinalizeOptions { AutoAdvance = false },
            Idempotent(key, "finalize"), cancellationToken);
        return await PayAsync(account, invoice.Id, Idempotent(key, "pay"), cancellationToken);
    }

    /// <inheritdoc />
    public Task<PaymentCharge> RetryChargeAsync(PaymentRef invoice, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(invoice);
        return PayAsync(accounts.Get(invoice.Account), invoice.Id, null, cancellationToken);
    }

    /// <summary>
    ///     Paga a fatura off-session no cartão padrão. Recusa (<c>card_error</c>) vira <see cref="ChargeStatus.Failed" />
    ///     ou, se o banco pediu autenticação, <see cref="ChargeStatus.RequiresAction" />; fatura que já estava paga vira
    ///     <see cref="ChargeStatus.Paid" />. Os demais erros propagam.
    /// </summary>
    private static async Task<PaymentCharge> PayAsync(StripeAccount account, string invoiceId, RequestOptions? requestOptions,
        CancellationToken cancellationToken) {
        try {
            var paid = await account.Client.V1.Invoices.PayAsync(invoiceId, new InvoicePayOptions { OffSession = true },
                requestOptions, cancellationToken);
            return ToCharge(account.Name, paid, paid.Status == "paid" ? ChargeStatus.Paid : ChargeStatus.Pending);
        } catch (StripeException e) when (e.StripeError?.Type == "card_error") {
            var invoice = await account.Client.V1.Invoices.GetAsync(invoiceId, cancellationToken: cancellationToken);
            var status = e.StripeError.Code == "authentication_required" ? ChargeStatus.RequiresAction : ChargeStatus.Failed;
            return ToCharge(account.Name, invoice, status, e.StripeError.DeclineCode ?? e.StripeError.Code, e.StripeError.Message);
        } catch (StripeException e) when (e.StripeError?.Type == "invalid_request_error") {
            var invoice = await account.Client.V1.Invoices.GetAsync(invoiceId, cancellationToken: cancellationToken);
            if (invoice.Status != "paid") throw;
            return ToCharge(account.Name, invoice, ChargeStatus.Paid);
        }
    }

    private static PaymentCharge ToCharge(string account, Invoice invoice, ChargeStatus status, string? code = null, string? message = null) =>
        new(new PaymentRef(account, invoice.Id), new Money(invoice.Total, invoice.Currency), status, code, message);

    private static RequestOptions Idempotent(string key, string step) => new() { IdempotencyKey = $"aedis:charge:{key}:{step}" };
}
