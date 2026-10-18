namespace Aedis.Payments.Abstractions;

/// <summary>
///     Recebe a notificação crua do provedor, verifica a assinatura, descarta duplicatas pelo
///     <see cref="IPaymentEventStore" /> e entrega o evento tipado a cada <see cref="IPaymentEventHandler" />.
/// </summary>
/// <example>
///     <code>
///     app.MapPost("/webhooks/pagamentos", async (HttpRequest request, IPaymentWebhookProcessor processor, CancellationToken ct) => {
///         var payload = await new StreamReader(request.Body).ReadToEndAsync(ct);
///         try {
///             await processor.ProcessAsync(payload, request.Headers["Stripe-Signature"].ToString(), ct);
///             return Results.Ok();
///         } catch (PaymentWebhookSignatureException) {
///             return Results.BadRequest();
///         }
///     }).AllowAnonymous();
///     </code>
/// </example>
public interface IPaymentWebhookProcessor
{
    /// <summary>
    ///     Processa a notificação. Exceção de handler propaga (responda 5xx) e libera o evento para o provedor
    ///     reenviar.
    /// </summary>
    /// <param name="payload">Corpo cru da requisição, exatamente como recebido (a assinatura cobre os bytes).</param>
    /// <param name="signatureHeader">Cabeçalho de assinatura do provedor.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <exception cref="PaymentWebhookSignatureException">Assinatura ausente, inválida ou expirada.</exception>
    Task<WebhookOutcome> ProcessAsync(string payload, string signatureHeader, CancellationToken cancellationToken = default);
}
