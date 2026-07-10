using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Aedis.Messaging.AwsSqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider AWS SQS/SNS do Aedis.
/// </summary>
public static class AwsSqsServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o broker AWS SQS/SNS (publish/subscribe com auto-detecção topic/queue), a factory de
    ///     clientes, o admin helper, o consumer manager e o health check. Lê as opções da seção <c>Aws</c>.
    ///     O broker é exposto como <see cref="IMessageBrokerService" /> padrão (via TryAdd) e keyed
    ///     <c>"awssqs"</c>. Reusa um <see cref="MessageSerializerResolver" /> já registrado, se houver.
    /// </summary>
    public static IServiceCollection AddAedisAwsSqs(this IServiceCollection services, IConfiguration configuration) {
        services.AddOptions<AwsSqsOptions>()
            .Bind(configuration.GetSection(AwsSqsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IAwsPubSubFactory, AwsPubSubFactory>();
        services.TryAddSingleton<AwsSqsAdministrationHelper>();

        services.TryAddSingleton(sp => new AwsSqsConsumerManager(
            sp.GetRequiredService<IAwsPubSubFactory>(),
            sp.GetRequiredService<Options.IOptions<AwsSqsOptions>>(),
            sp.GetRequiredService<ILogger<AwsSqsConsumerManager>>(),
            sp.GetService<MessageSerializerResolver>() ?? MessageSerializerResolver.CreateDefault(),
            ResolveEncoders(sp)));

        services.TryAddSingleton(sp => new AwsSqsMessageBrokerService(
            sp.GetRequiredService<IAwsPubSubFactory>(),
            sp.GetRequiredService<ILogger<AwsSqsMessageBrokerService>>(),
            sp.GetRequiredService<AwsSqsAdministrationHelper>(),
            sp.GetRequiredService<AwsSqsConsumerManager>(),
            sp.GetService<MessageSerializerResolver>(),
            ResolveEncoders(sp)));

        services.TryAddSingleton<IMessageBrokerService>(sp => sp.GetRequiredService<AwsSqsMessageBrokerService>());
        services.AddKeyedSingleton<IMessageBrokerService>("awssqs",
            (sp, _) => sp.GetRequiredService<AwsSqsMessageBrokerService>());

        services.AddHealthChecks()
            .AddCheck<AwsSqsHealthCheck>("awssqs", tags: ["ready"], timeout: TimeSpan.FromSeconds(30));

        return services;
    }

    private static MessageEncoderResolver ResolveEncoders(IServiceProvider sp) {
        if (sp.GetService<MessageEncoderResolver>() is { } registered)
            return registered;

        var options = sp.GetRequiredService<Options.IOptions<AwsSqsOptions>>().Value;
        return new MessageEncoderResolver([new IdentityMessageEncoder(), new GzipMessageEncoder()],
            options.CompressionEnabled, options.CompressionThresholdBytes);
    }
}
