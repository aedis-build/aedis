namespace Aedis.Payments.Abstractions;

/// <summary>
///     Pedido de página de pagamento hospedada pelo provedor para o cliente assinar um plano: coleta o cartão, cobra a
///     primeira mensalidade e cria a assinatura. Ao concluir, o provedor redireciona para <paramref name="SuccessUrl" />
///     e o resultado chega por webhook (<see cref="SubscriptionChanged" />, <see cref="PaymentSucceeded" />).
/// </summary>
/// <param name="Customer">Cliente criado por <see cref="IPaymentGateway.CreateCustomerAsync" />.</param>
/// <param name="Plan">Preço mensal do plano.</param>
/// <param name="SuccessUrl">Para onde o cliente volta ao concluir.</param>
/// <param name="CancelUrl">Para onde o cliente volta se desistir.</param>
public sealed record SubscriptionCheckout(PaymentRef Customer, PlanPrice Plan, Uri SuccessUrl, Uri CancelUrl);
