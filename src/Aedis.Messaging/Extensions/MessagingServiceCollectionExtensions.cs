using Aedis.Messaging;
using Aedis.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI dos consumers de mensagem do Aedis: um <see cref="MessageConsumerService{TMessage}" />
///     hospedado por assinatura, com o handler adaptado automaticamente para escopo por mensagem quando ele
///     não é singleton. Requer um <see cref="IMessageBrokerService" /> registrado pelo provider escolhido.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    ///     Registra <typeparamref name="THandler" /> como <see cref="IMessageHandler{T}" /> de
    ///     <typeparamref name="TMessage" /> (no ciclo de vida <paramref name="handlerLifetime" />, scoped por
    ///     padrão) e um consumer hospedado que assina <paramref name="subscription" /> pelo tempo de vida do
    ///     host. Handlers scoped/transient são envolvidos por <see cref="ScopedMessageHandler{T}" /> — um
    ///     escopo de DI por mensagem. Chame uma vez por assinatura; cada chamada cria um consumer.
    /// </summary>
    /// <typeparam name="TMessage">Tipo da mensagem consumida.</typeparam>
    /// <typeparam name="THandler">Handler que processa a mensagem.</typeparam>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="subscription">Fila, exchange e routing key(s) da assinatura.</param>
    /// <param name="retry">Política de retry/dead-letter do provider; padrão do framework quando omitida.</param>
    /// <param name="handlerLifetime">Ciclo de vida do handler. Padrão <see cref="ServiceLifetime.Scoped" />.</param>
    /// <param name="configure">Ajuste opcional dos intervalos de reassinatura.</param>
    public static IServiceCollection AddAedisMessageConsumer<TMessage, THandler>(this IServiceCollection services,
        MessageMetadata subscription, ConsumerRetryOptions? retry = null,
        ServiceLifetime handlerLifetime = ServiceLifetime.Scoped,
        Func<MessageConsumerOptions, MessageConsumerOptions>? configure = null)
        where TMessage : class, IMessage
        where THandler : class, IMessageHandler<TMessage> {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(subscription);

        services.TryAdd(ServiceDescriptor.Describe(typeof(IMessageHandler<TMessage>), typeof(THandler), handlerLifetime));

        var options = new MessageConsumerOptions(subscription) { Retry = retry ?? new ConsumerRetryOptions() };
        options = configure?.Invoke(options) ?? options;

        services.AddSingleton<IHostedService>(sp => {
            var handler = handlerLifetime == ServiceLifetime.Singleton
                ? sp.GetRequiredService<IMessageHandler<TMessage>>()
                : new ScopedMessageHandler<TMessage>(sp.GetRequiredService<IServiceScopeFactory>());

            return new MessageConsumerService<TMessage>(sp.GetRequiredService<IMessageBrokerService>(), handler, options,
                sp.GetRequiredService<ILogger<MessageConsumerService<TMessage>>>());
        });

        return services;
    }
}
