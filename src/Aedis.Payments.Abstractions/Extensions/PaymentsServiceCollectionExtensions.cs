using Aedis.Payments.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Ponto de entrada do registro do módulo de pagamentos.</summary>
public static class PaymentsServiceCollectionExtensions
{
    /// <summary>
    ///     Começa o registro do módulo de pagamentos; escolha o provedor em seguida (ex.:
    ///     <c>services.AddAedisPayments().WithStripe(configuration)</c>). A aplicação registra os seus
    ///     <see cref="IPaymentEventHandler" /> e um <see cref="IPaymentEventStore" /> durável.
    /// </summary>
    public static PaymentsBuilder AddAedisPayments(this IServiceCollection services) {
        ArgumentNullException.ThrowIfNull(services);
        return new PaymentsBuilder(services);
    }
}
