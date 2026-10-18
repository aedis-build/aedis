using Aedis.Payments.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace Aedis.Payments.Stripe;

/// <summary>
///     <see cref="IPaymentWebhookProcessor" /> da Stripe. A assinatura (<c>Stripe-Signature</c>, tolerância de 5 minutos)
///     é conferida contra o segredo de cada conta; sem acerto, falha fechado. O evento é reservado no
///     <see cref="IPaymentEventStore" /> antes dos handlers e concluído só se todos passarem — falha libera para o
///     reenvio da Stripe. Traduz <c>invoice.paid</c>, <c>invoice.payment_failed</c>, <c>customer.subscription.*</c>,
///     e torna padrão do cliente o cartão de <c>checkout.session.completed</c> e <c>setup_intent.succeeded</c>
///     (a fatura avulsa e a assinatura passam a usar o mesmo cartão).
/// </summary>
internal sealed class StripeWebhookProcessor(
    StripeAccounts accounts,
    IServiceScopeFactory scopeFactory,
    ILogger<StripeWebhookProcessor> logger) : IPaymentWebhookProcessor
{
    /// <summary>Cabeçalho onde a Stripe envia a assinatura.</summary>
    public const string SignatureHeader = "Stripe-Signature";

    private const long ToleranceSeconds = 300;

    /// <inheritdoc />
    public async Task<WebhookOutcome> ProcessAsync(string payload, string signatureHeader, CancellationToken cancellationToken = default) {
        if (string.IsNullOrEmpty(payload) || string.IsNullOrWhiteSpace(signatureHeader))
            throw new PaymentWebhookSignatureException("Notificação sem corpo ou sem assinatura.");

        var account = Verify(payload, signatureHeader);
        var stripeEvent = EventUtility.ParseEvent(payload);

        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetService<IPaymentEventStore>()
                    ?? throw new InvalidOperationException(
                        "Nenhum IPaymentEventStore registrado: registre um store durável (ou InMemoryPaymentEventStore em testes) para deduplicar webhooks.");

        var eventKey = $"stripe:{account.Name}:{stripeEvent.Id}";
        if (!await store.TryClaimAsync(eventKey, cancellationToken)) return WebhookOutcome.Duplicate;

        try {
            var paymentEvent = await TranslateAsync(account, stripeEvent, cancellationToken);
            if (paymentEvent is null) {
                logger.LogDebug("Evento Stripe {EventType} ({EventId}) ignorado.", stripeEvent.Type, stripeEvent.Id);
                await store.CompleteAsync(eventKey, cancellationToken);
                return WebhookOutcome.Ignored;
            }

            foreach (var handler in scope.ServiceProvider.GetServices<IPaymentEventHandler>())
                await handler.HandleAsync(paymentEvent, cancellationToken);

            await store.CompleteAsync(eventKey, cancellationToken);
            return WebhookOutcome.Processed;
        } catch {
            await store.ReleaseAsync(eventKey, CancellationToken.None);
            throw;
        }
    }

    private StripeAccount Verify(string payload, string signatureHeader) {
        foreach (var account in accounts.All) {
            try {
                EventUtility.ValidateSignature(payload, signatureHeader, account.Options.WebhookSecret, ToleranceSeconds);
                return account;
            } catch (StripeException) {
            }
        }

        throw new PaymentWebhookSignatureException("Assinatura do webhook não confere com nenhuma conta Stripe configurada.");
    }

    private static async Task<PaymentEvent?> TranslateAsync(StripeAccount account, Event stripeEvent, CancellationToken cancellationToken) {
        var at = StripeMapper.AsUtc(stripeEvent.Created);
        var name = account.Name;
        return (stripeEvent.Type, stripeEvent.Data.Object) switch {
            (EventTypes.InvoicePaid, Invoice invoice) => new PaymentSucceeded(name, stripeEvent.Id, at,
                new PaymentRef(name, invoice.Id), new PaymentRef(name, invoice.CustomerId),
                invoice.Parent?.SubscriptionDetails?.SubscriptionId, new Money(invoice.AmountPaid, invoice.Currency)),
            (EventTypes.InvoicePaymentFailed, Invoice invoice) => new PaymentFailed(name, stripeEvent.Id, at,
                new PaymentRef(name, invoice.Id), new PaymentRef(name, invoice.CustomerId),
                invoice.Parent?.SubscriptionDetails?.SubscriptionId, new Money(invoice.AmountDue, invoice.Currency),
                (int)invoice.AttemptCount, invoice.NextPaymentAttempt is { } next ? StripeMapper.AsUtc(next) : null),
            (EventTypes.CustomerSubscriptionCreated or EventTypes.CustomerSubscriptionUpdated, Subscription subscription) =>
                new SubscriptionChanged(name, stripeEvent.Id, at, StripeMapper.ToSubscription(name, subscription)),
            (EventTypes.CustomerSubscriptionDeleted, Subscription subscription) =>
                new SubscriptionCanceled(name, stripeEvent.Id, at, StripeMapper.ToSubscription(name, subscription)),
            (EventTypes.CheckoutSessionCompleted, Session { Mode: "subscription", SubscriptionId: { } subscriptionId, CustomerId: { } customerId }) =>
                await AdoptSubscriptionCardAsync(account, stripeEvent, at, customerId, subscriptionId, cancellationToken),
            (EventTypes.SetupIntentSucceeded, SetupIntent { CustomerId: { } customerId, PaymentMethodId: { } paymentMethodId }) =>
                await SetDefaultCardAsync(account, stripeEvent, at, customerId, paymentMethodId, cancellationToken),
            _ => null,
        };
    }

    /// <summary>
    ///     O Checkout grava o cartão só na assinatura; ele vira o padrão do cliente e sai da assinatura, para a fatura
    ///     avulsa e a troca de cartão terem uma única fonte.
    /// </summary>
    private static async Task<PaymentEvent?> AdoptSubscriptionCardAsync(StripeAccount account, Event stripeEvent, DateTimeOffset at,
        string customerId, string subscriptionId, CancellationToken cancellationToken) {
        var subscription = await account.Client.V1.Subscriptions.GetAsync(subscriptionId, cancellationToken: cancellationToken);
        if (subscription.DefaultPaymentMethodId is not { } paymentMethodId) return null;

        var updated = await SetDefaultCardAsync(account, stripeEvent, at, customerId, paymentMethodId, cancellationToken);
        await account.Client.V1.Subscriptions.UpdateAsync(subscriptionId,
            new SubscriptionUpdateOptions { DefaultPaymentMethod = string.Empty }, cancellationToken: cancellationToken);
        return updated;
    }

    private static async Task<PaymentEvent> SetDefaultCardAsync(StripeAccount account, Event stripeEvent, DateTimeOffset at,
        string customerId, string paymentMethodId, CancellationToken cancellationToken) {
        await account.Client.V1.Customers.UpdateAsync(customerId, new CustomerUpdateOptions {
            InvoiceSettings = new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = paymentMethodId },
        }, cancellationToken: cancellationToken);
        return new PaymentMethodUpdated(account.Name, stripeEvent.Id, at, new PaymentRef(account.Name, customerId), paymentMethodId);
    }
}
