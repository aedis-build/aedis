namespace Aedis.Payments.Abstractions;

/// <summary>Uma fatura (mensalidade, proporcional de troca de plano ou cobrança avulsa) foi paga.</summary>
/// <param name="Account">Conta do provedor.</param>
/// <param name="EventId">Id do evento.</param>
/// <param name="OccurredAt">Quando aconteceu.</param>
/// <param name="Invoice">Fatura paga.</param>
/// <param name="Customer">Cliente pagador.</param>
/// <param name="SubscriptionId">Assinatura da fatura; <c>null</c> em cobrança avulsa.</param>
/// <param name="Amount">Valor pago.</param>
public sealed record PaymentSucceeded(
    string Account,
    string EventId,
    DateTimeOffset OccurredAt,
    PaymentRef Invoice,
    PaymentRef Customer,
    string? SubscriptionId,
    Money Amount) : PaymentEvent(Account, EventId, OccurredAt);
