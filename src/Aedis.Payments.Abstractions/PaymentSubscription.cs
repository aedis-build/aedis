namespace Aedis.Payments.Abstractions;

/// <summary>Fotografia de uma assinatura no provedor.</summary>
/// <param name="Subscription">Referência da assinatura (conta + id).</param>
/// <param name="Customer">Cliente pagador, na mesma conta.</param>
/// <param name="Status">Estado normalizado.</param>
/// <param name="PriceId">Preço vigente no provedor (inclusive os montados por <see cref="PlanPrice.Custom" />).</param>
/// <param name="CurrentPeriodEnd">Fim do período pago; com <paramref name="CancelAtPeriodEnd" />, o acesso vai até aqui.</param>
/// <param name="CancelAtPeriodEnd">Cancelamento agendado para o fim do período.</param>
public sealed record PaymentSubscription(
    PaymentRef Subscription,
    PaymentRef Customer,
    SubscriptionStatus Status,
    string? PriceId,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);
