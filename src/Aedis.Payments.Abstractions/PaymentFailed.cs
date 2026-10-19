namespace Aedis.Payments.Abstractions;

/// <summary>
///     A cobrança de uma fatura falhou. Ponto de partida da régua de cobrança da aplicação: agendar
///     <see cref="IPaymentGateway.RetryChargeAsync" />, avisar o cliente, suspender o serviço.
/// </summary>
/// <param name="Account">Conta do provedor.</param>
/// <param name="EventId">Id do evento.</param>
/// <param name="OccurredAt">Quando aconteceu.</param>
/// <param name="Invoice">Fatura em aberto.</param>
/// <param name="Customer">Cliente pagador.</param>
/// <param name="SubscriptionId">Assinatura da fatura; <c>null</c> em cobrança avulsa.</param>
/// <param name="AmountDue">Valor em aberto.</param>
/// <param name="AttemptCount">Tentativas já feitas nesta fatura.</param>
/// <param name="NextAttemptAt">Próxima tentativa automática do provedor, se ele tiver uma agendada.</param>
public sealed record PaymentFailed(
    string Account,
    string EventId,
    DateTimeOffset OccurredAt,
    PaymentRef Invoice,
    PaymentRef Customer,
    string? SubscriptionId,
    Money AmountDue,
    int AttemptCount,
    DateTimeOffset? NextAttemptAt) : PaymentEvent(Account, EventId, OccurredAt);
