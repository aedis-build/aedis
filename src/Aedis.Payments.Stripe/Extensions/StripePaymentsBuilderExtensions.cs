using Aedis.Payments.Abstractions;
using Aedis.Payments.Stripe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registro do provider Stripe no módulo de pagamentos.</summary>
public static class StripePaymentsBuilderExtensions
{
    /// <summary>Cabeçalho onde a Stripe envia a assinatura do webhook.</summary>
    public const string SignatureHeader = StripeWebhookProcessor.SignatureHeader;

    /// <summary>
    ///     Usa a Stripe como <see cref="IPaymentGateway" /> e <see cref="IPaymentWebhookProcessor" />: vincula
    ///     <c>Payments:Stripe</c> (<see cref="StripeOptions" />, validado na subida — chave live só com
    ///     <c>AllowLiveMode</c>) e cria um cliente da API por conta. A aplicação registra um
    ///     <see cref="IPaymentEventStore" /> e os seus <see cref="IPaymentEventHandler" />.
    /// </summary>
    public static PaymentsBuilder WithStripe(this PaymentsBuilder builder, IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var services = builder.Services;
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, StripeOptionsValidator>());

        services.TryAddSingleton(sp => new StripeAccounts(sp.GetRequiredService<IOptions<StripeOptions>>().Value));
        services.TryAddSingleton<IPaymentGateway, StripePaymentGateway>();
        services.TryAddSingleton<IPaymentWebhookProcessor, StripeWebhookProcessor>();
        return builder;
    }
}
