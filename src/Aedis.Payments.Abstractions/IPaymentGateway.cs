namespace Aedis.Payments.Abstractions;

/// <summary>
///     Porta de cobrança recorrente de um SaaS: cliente por tenant, assinatura mensal, troca de plano com
///     proporcionalidade, cancelamento no fim do período, troca de cartão e cobrança avulsa off-session. O provedor
///     é escolhido na composition root (ex.: <c>services.AddAedisPayments().WithStripe(configuration)</c>); o
///     resultado assíncrono (pagou, falhou, assinatura mudou) chega por <see cref="IPaymentEventHandler" />.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Cria o cliente pagador na conta pedida e devolve a referência a guardar junto do tenant.</summary>
    Task<PaymentRef> CreateCustomerAsync(NewCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Abre a página de pagamento hospedada para o cliente assinar o plano e devolve a URL para redirecioná-lo.
    /// </summary>
    Task<Uri> CreateSubscriptionCheckoutAsync(SubscriptionCheckout checkout, CancellationToken cancellationToken = default);

    /// <summary>Lê a assinatura no provedor.</summary>
    Task<PaymentSubscription> GetSubscriptionAsync(PaymentRef subscription, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Troca o plano com efeito imediato: a diferença proporcional ao resto do período é faturada e cobrada agora
    ///     (no rebaixamento, o crédito abate a próxima fatura). Também desfaz um cancelamento agendado.
    /// </summary>
    Task<PaymentSubscription> ChangePlanAsync(PaymentRef subscription, PlanPrice plan, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Agenda o cancelamento para o fim do período pago: nada mais é cobrado e o acesso continua até
    ///     <see cref="PaymentSubscription.CurrentPeriodEnd" />, quando chega <see cref="SubscriptionCanceled" />.
    /// </summary>
    Task<PaymentSubscription> CancelAtPeriodEndAsync(PaymentRef subscription, CancellationToken cancellationToken = default);

    /// <summary>Prepara a coleta de um cartão novo pelo front-end (troca de cartão).</summary>
    Task<CardSetup> CreateCardSetupAsync(PaymentRef customer, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fatura e cobra, sem o cliente presente, o cartão padrão dele. Recusa do cartão <b>não</b> lança exceção:
    ///     volta <see cref="ChargeStatus.Failed" /> com o código, e o mesmo resultado chega por <see cref="PaymentFailed" />.
    /// </summary>
    Task<PaymentCharge> ChargeAsync(OneOffCharge charge, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Tenta de novo uma fatura em aberto (avulsa ou da assinatura) no cartão padrão atual — a régua de cobrança
    ///     da aplicação chama isto nos dias de retentativa.
    /// </summary>
    Task<PaymentCharge> RetryChargeAsync(PaymentRef invoice, CancellationToken cancellationToken = default);
}
