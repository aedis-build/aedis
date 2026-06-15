using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Aedis.Messaging.RabbitMq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Messaging.RabbitMq.Tests;

/// <summary>
///     Verifica a extensão de DI <c>AddAedisRabbitMq()</c> — registro de options, serializers, resolver,
///     consumer manager e broker. Não resolve o broker (que conecta), apenas valida as injeções.
/// </summary>
public sealed class RabbitMqRegistrationTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["RABBITMQ:Host"] = "localhost",
            ["RABBITMQ:Port"] = "5672",
            ["RABBITMQ:Username"] = "user",
            ["RABBITMQ:Password"] = "pass",
            ["RABBITMQ:VirtualHost"] = "/",
            ["RABBITMQ:PrefetchCount"] = "1",
            ["RABBITMQ:MaxChannels"] = "1",
            ["RABBITMQ:ChannelTimeoutSeconds"] = "15"
        }).Build();

    [Fact]
    public void AddAedisRabbitMq_registra_options_serializers_e_consumer_manager() {
        var services = new ServiceCollection().AddLogging();

        services.AddAedisRabbitMq(Config());
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value.Host.Should().Be("localhost");
        provider.GetRequiredService<MessageSerializerResolver>().Should().NotBeNull();
        provider.GetRequiredService<RabbitMqConsumerManager>().Should().NotBeNull();
        provider.GetServices<IMessageSerializer>().Should().HaveCount(4);

        services.Should().Contain(d => d.ServiceType == typeof(IMessageBrokerService));
    }

    [Fact]
    public void AddAedisMessageSerialization_e_idempotente() {
        var services = new ServiceCollection();

        services.AddAedisMessageSerialization();
        services.AddAedisMessageSerialization();

        services.Count(d => d.ServiceType == typeof(MessageSerializerResolver)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IMessageSerializer)).Should().Be(4);
    }
}
