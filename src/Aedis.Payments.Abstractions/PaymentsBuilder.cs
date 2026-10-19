using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Payments.Abstractions;

/// <summary>
///     Ajustes fluentes após <c>AddAedisPayments</c>. Pacotes de provider estendem este builder (ex.:
///     <c>WithStripe()</c>) registrando o <see cref="IPaymentGateway" /> e o <see cref="IPaymentWebhookProcessor" />.
/// </summary>
public sealed class PaymentsBuilder(IServiceCollection services)
{
    /// <summary>Coleção de serviços em configuração.</summary>
    public IServiceCollection Services { get; } = services ?? throw new ArgumentNullException(nameof(services));
}
