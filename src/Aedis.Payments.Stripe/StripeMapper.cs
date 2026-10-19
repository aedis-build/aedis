using Aedis.Payments.Abstractions;
using Stripe;
using Stripe.Checkout;

namespace Aedis.Payments.Stripe;

/// <summary>Traduções entre os objetos da Stripe e os contratos de <c>Aedis.Payments.Abstractions</c>.</summary>
internal static class StripeMapper
{
    /// <summary>Assinatura da Stripe → <see cref="PaymentSubscription" />; preço e período vêm do único item.</summary>
    public static PaymentSubscription ToSubscription(string account, Subscription subscription) {
        var item = subscription.Items?.Data?.FirstOrDefault();
        return new PaymentSubscription(
            new PaymentRef(account, subscription.Id),
            new PaymentRef(account, subscription.CustomerId),
            ToStatus(subscription.Status),
            item?.Price?.Id,
            item is null ? null : AsUtc(item.CurrentPeriodEnd),
            subscription.CancelAtPeriodEnd);
    }

    /// <summary>Status textual da Stripe → <see cref="SubscriptionStatus" />.</summary>
    public static SubscriptionStatus ToStatus(string? status) => status switch {
        "incomplete" => SubscriptionStatus.Incomplete,
        "incomplete_expired" => SubscriptionStatus.IncompleteExpired,
        "trialing" => SubscriptionStatus.Trialing,
        "active" => SubscriptionStatus.Active,
        "past_due" => SubscriptionStatus.PastDue,
        "unpaid" => SubscriptionStatus.Unpaid,
        "paused" => SubscriptionStatus.Paused,
        "canceled" => SubscriptionStatus.Canceled,
        _ => SubscriptionStatus.Unknown,
    };

    /// <summary>Item de Checkout para o plano: preço do catálogo ou preço mensal montado na hora.</summary>
    public static SessionLineItemOptions ToCheckoutLine(PlanPrice plan) => plan.PriceId is { } priceId
        ? new SessionLineItemOptions { Price = priceId, Quantity = 1 }
        : new SessionLineItemOptions {
            Quantity = 1,
            PriceData = new SessionLineItemPriceDataOptions {
                Product = plan.ProductId,
                Currency = ToStripeCurrency(plan.MonthlyAmount!.Currency),
                UnitAmount = plan.MonthlyAmount.Amount,
                Recurring = new SessionLineItemPriceDataRecurringOptions { Interval = "month" },
            },
        };

    /// <summary>Item da assinatura para a troca de plano, mantendo o mesmo item (a Stripe calcula o proporcional).</summary>
    public static SubscriptionItemOptions ToSubscriptionItem(string itemId, PlanPrice plan) => plan.PriceId is { } priceId
        ? new SubscriptionItemOptions { Id = itemId, Price = priceId }
        : new SubscriptionItemOptions {
            Id = itemId,
            PriceData = new SubscriptionItemPriceDataOptions {
                Product = plan.ProductId,
                Currency = ToStripeCurrency(plan.MonthlyAmount!.Currency),
                UnitAmount = plan.MonthlyAmount.Amount,
                Recurring = new SubscriptionItemPriceDataRecurringOptions { Interval = "month" },
            },
        };

    /// <summary>A Stripe usa ISO 4217 em minúsculas.</summary>
    public static string ToStripeCurrency(string currency) => Money.NormalizeCurrency(currency).ToLowerInvariant();

    /// <summary>Datas do SDK chegam como <see cref="DateTime" /> em UTC.</summary>
    public static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
