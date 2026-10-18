namespace Aedis.Payments.Abstractions;

/// <summary>Evento de pagamento vindo do webhook do provedor, já verificado e deduplicado.</summary>
/// <param name="Account">Conta do provedor que emitiu o evento.</param>
/// <param name="EventId">Id do evento no provedor (chave de idempotência).</param>
/// <param name="OccurredAt">Quando o provedor registrou o evento.</param>
public abstract record PaymentEvent(string Account, string EventId, DateTimeOffset OccurredAt);
