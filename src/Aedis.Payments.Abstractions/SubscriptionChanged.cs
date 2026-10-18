namespace Aedis.Payments.Abstractions;

/// <summary>Assinatura criada ou alterada (ativou, trocou de plano, entrou em atraso, cancelamento agendado).</summary>
/// <param name="Account">Conta do provedor.</param>
/// <param name="EventId">Id do evento.</param>
/// <param name="OccurredAt">Quando aconteceu.</param>
/// <param name="Subscription">Estado atual da assinatura.</param>
public sealed record SubscriptionChanged(
    string Account,
    string EventId,
    DateTimeOffset OccurredAt,
    PaymentSubscription Subscription) : PaymentEvent(Account, EventId, OccurredAt);
