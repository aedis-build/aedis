namespace Aedis.Payments.Abstractions;

/// <summary>Assinatura encerrada (fim do período após cancelamento agendado, ou encerramento pelo provedor).</summary>
/// <param name="Account">Conta do provedor.</param>
/// <param name="EventId">Id do evento.</param>
/// <param name="OccurredAt">Quando aconteceu.</param>
/// <param name="Subscription">Último estado da assinatura.</param>
public sealed record SubscriptionCanceled(
    string Account,
    string EventId,
    DateTimeOffset OccurredAt,
    PaymentSubscription Subscription) : PaymentEvent(Account, EventId, OccurredAt);
