namespace Aedis.Payments.Abstractions;

/// <summary>Resultado de <see cref="IPaymentGateway.ChargeAsync" /> ou <see cref="IPaymentGateway.RetryChargeAsync" />.</summary>
/// <param name="Invoice">Fatura no provedor; use-a para tentar de novo.</param>
/// <param name="Amount">Valor total da fatura.</param>
/// <param name="Status">Situação após a tentativa.</param>
/// <param name="FailureCode">Código da recusa do provedor (ex.: <c>card_declined</c>), quando houver.</param>
/// <param name="FailureMessage">Mensagem da recusa, segura para o cliente.</param>
public sealed record PaymentCharge(
    PaymentRef Invoice,
    Money Amount,
    ChargeStatus Status,
    string? FailureCode = null,
    string? FailureMessage = null);
