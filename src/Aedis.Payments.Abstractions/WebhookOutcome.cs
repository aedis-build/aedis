namespace Aedis.Payments.Abstractions;

/// <summary>O que <see cref="IPaymentWebhookProcessor.ProcessAsync" /> fez com uma notificação válida.</summary>
public enum WebhookOutcome
{
    /// <summary>Evento tipado entregue aos handlers.</summary>
    Processed,

    /// <summary>Evento já processado (ou em processamento) — ignorado sem chamar os handlers.</summary>
    Duplicate,

    /// <summary>Tipo de evento que o módulo não traduz; aceito e descartado.</summary>
    Ignored,
}
