namespace Aedis.Payments.Abstractions;

/// <summary>
///     O cliente tem um cartão padrão novo (assinatura concluída ou troca de cartão). Bom momento para tentar de novo
///     faturas em aberto.
/// </summary>
/// <param name="Account">Conta do provedor.</param>
/// <param name="EventId">Id do evento.</param>
/// <param name="OccurredAt">Quando aconteceu.</param>
/// <param name="Customer">Cliente.</param>
/// <param name="PaymentMethodId">Id do novo meio de pagamento padrão.</param>
public sealed record PaymentMethodUpdated(
    string Account,
    string EventId,
    DateTimeOffset OccurredAt,
    PaymentRef Customer,
    string PaymentMethodId) : PaymentEvent(Account, EventId, OccurredAt);
