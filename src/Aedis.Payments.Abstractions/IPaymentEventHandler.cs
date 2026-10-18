namespace Aedis.Payments.Abstractions;

/// <summary>
///     Reage aos eventos de pagamento (régua de cobrança, liberar ou suspender acesso). Registre quantos quiser; todos
///     recebem cada evento, em ordem de registro, num escopo de DI por notificação. Use pattern matching sobre o tipo:
///     <see cref="PaymentSucceeded" />, <see cref="PaymentFailed" />, <see cref="SubscriptionChanged" />,
///     <see cref="SubscriptionCanceled" />, <see cref="PaymentMethodUpdated" />.
/// </summary>
public interface IPaymentEventHandler
{
    /// <summary>Trata o evento. Lançar faz o provedor reenviar a notificação mais tarde.</summary>
    Task HandleAsync(PaymentEvent paymentEvent, CancellationToken cancellationToken);
}
