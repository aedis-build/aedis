using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Aedis.Messaging.AzureServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider Azure Service Bus do Aedis.
/// </summary>
public static class ServiceBusServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o broker Azure Service Bus (publish/subscribe topic/queue com auto-provisionamento),
    ///     o admin helper e o health check. Lê as opções da seção <c>ServiceBus</c>. O broker é exposto
    ///     como <see cref="IMessageBrokerService" /> padrão (via TryAdd) e keyed <c>"azureservicebus"</c>.
    ///     Reusa um <see cref="MessageSerializerResolver" /> já registrado, se houver.
    /// </summary>
    public static IServiceCollection AddAedisAzureServiceBus(this IServiceCollection services,
        IConfiguration configuration) {
        services.AddOptions<ServiceBusOptions>()
            .Bind(configuration.GetSection(ServiceBusOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<ServiceBusAdministrationHelper>();

        services.TryAddSingleton(sp => new ServiceBusMessageBrokerService(
            sp.GetRequiredService<IOptions<ServiceBusOptions>>(),
            sp.GetRequiredService<ILogger<ServiceBusMessageBrokerService>>(),
            sp.GetService<ServiceBusAdministrationHelper>(),
            sp.GetService<IHostApplicationLifetime>(),
            sp.GetService<MessageSerializerResolver>(),
            sp.GetService<ILoggerFactory>()));

        services.TryAddSingleton<IMessageBrokerService>(sp =>
            sp.GetRequiredService<ServiceBusMessageBrokerService>());
        services.AddKeyedSingleton<IMessageBrokerService>("azureservicebus",
            (sp, _) => sp.GetRequiredService<ServiceBusMessageBrokerService>());

        services.AddHealthChecks()
            .AddCheck<ServiceBusHealthCheck>("azureservicebus", tags: ["ready"], timeout: TimeSpan.FromSeconds(30));

        return services;
    }
}
