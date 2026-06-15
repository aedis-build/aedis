using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Aedis.Messaging.IbmMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider IBM MQ do Aedis.
/// </summary>
public static class IbmMqServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o broker IBM MQ (publish/subscribe com consumer inteligente) e o health check.
    ///     Lê as opções da seção <c>IBMMQ</c> da configuração. O broker é exposto como
    ///     <see cref="IMessageBrokerService" /> padrão (sem clobber, via TryAdd) e também keyed
    ///     <c>"ibmmq"</c> — útil quando convive com outros brokers. Reusa um
    ///     <see cref="MessageSerializerResolver" /> já registrado, se houver; senão usa o conjunto padrão.
    /// </summary>
    public static IServiceCollection AddAedisIbmMq(this IServiceCollection services, IConfiguration configuration) {
        services.AddOptions<IbmMqOptions>()
            .Bind(configuration.GetSection(IbmMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(sp => new IbmMqMessageBrokerService(
            sp.GetRequiredService<IOptions<IbmMqOptions>>(),
            sp.GetRequiredService<ILogger<IbmMqMessageBrokerService>>(),
            sp.GetService<MessageSerializerResolver>(),
            sp.GetService<ILoggerFactory>()));

        services.TryAddSingleton<IMessageBrokerService>(sp => sp.GetRequiredService<IbmMqMessageBrokerService>());
        services.AddKeyedSingleton<IMessageBrokerService>("ibmmq",
            (sp, _) => sp.GetRequiredService<IbmMqMessageBrokerService>());

        services.AddHealthChecks()
            .AddCheck<IbmMqHealthCheck>("ibmmq", tags: ["ready"], timeout: TimeSpan.FromSeconds(30));

        return services;
    }
}
