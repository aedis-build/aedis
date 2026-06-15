using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Aedis.Messaging.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider RabbitMQ do Aedis.
/// </summary>
public static class RabbitMqServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o broker RabbitMQ (publish/subscribe), o gerenciador de consumers, as estratégias de
    ///     serialização e o health check. Lê as opções da seção <c>RABBITMQ</c> da configuração.
    /// </summary>
    public static IServiceCollection AddAedisRabbitMq(this IServiceCollection services, IConfiguration configuration) {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAedisMessageSerialization();

        services.TryAddSingleton(sp => new RabbitMqConsumerManager(
            sp.GetRequiredService<ILogger<RabbitMqConsumerManager>>(),
            sp.GetRequiredService<IOptions<RabbitMqOptions>>(),
            sp.GetRequiredService<MessageSerializerResolver>()));

        services.TryAddSingleton(sp => new RabbitMqMessageBrokerService(
            sp.GetRequiredService<IOptions<RabbitMqOptions>>(),
            sp.GetRequiredService<ILogger<RabbitMqMessageBrokerService>>(),
            sp.GetRequiredService<RabbitMqConsumerManager>(),
            sp.GetRequiredService<MessageSerializerResolver>(),
            sp.GetService<IHostApplicationLifetime>()));

        services.TryAddSingleton<IMessageBrokerService>(sp => sp.GetRequiredService<RabbitMqMessageBrokerService>());
        services.AddKeyedSingleton<IMessageBrokerService>("rabbitmq",
            (sp, _) => sp.GetRequiredService<RabbitMqMessageBrokerService>());

        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"], timeout: TimeSpan.FromSeconds(30));

        return services;
    }

    /// <summary>
    ///     Registra as estratégias de serialização de mensagem padrão (bytes → texto → MessagePack → JSON)
    ///     e o <see cref="MessageSerializerResolver" />. Idempotente — pode ser chamado por vários brokers.
    /// </summary>
    public static IServiceCollection AddAedisMessageSerialization(this IServiceCollection services) {
        if (services.Any(d => d.ServiceType == typeof(MessageSerializerResolver)))
            return services;

        services.AddSingleton<IMessageSerializer, RawBytesMessageSerializer>();
        services.AddSingleton<IMessageSerializer, PlainTextMessageSerializer>();
        services.AddSingleton<IMessageSerializer, MessagePackMessageSerializer>();
        services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.AddSingleton(sp => new MessageSerializerResolver(sp.GetServices<IMessageSerializer>()));

        return services;
    }
}
