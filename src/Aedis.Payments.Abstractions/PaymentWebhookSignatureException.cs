namespace Aedis.Payments.Abstractions;

/// <summary>
///     A notificação não tem assinatura válida de nenhuma conta configurada (ou está fora da janela de tempo). A
///     aplicação responde <c>400</c> e não processa nada — o webhook falha fechado.
/// </summary>
public sealed class PaymentWebhookSignatureException(string message, Exception? innerException = null)
    : Exception(message, innerException);
