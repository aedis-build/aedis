using Aedis.Messaging;
using Aedis.Messaging.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Aedis.Messaging.Tests;

/// <summary>
///     Consumer hospedado genérico e handler com escopo (sem broker real): a assinatura é refeita após falha
///     do broker, o shutdown encerra o loop sem erro, cada mensagem ganha um escopo de DI próprio e o registro
///     por <c>AddAedisMessageConsumer</c> monta tudo a partir do contêiner.
/// </summary>
public sealed class MessageConsumerServiceTests
{
    private static readonly MessageMetadata Subscription = new("ex", "fila", "chave");

    public sealed class PingMessage : MessageBase
    {
        public override string EventName => "ping";
    }

    public sealed class RecordingHandler : IMessageHandler<PingMessage>
    {
        public static readonly List<RecordingHandler> Instances = [];

        public RecordingHandler() => Instances.Add(this);

        public Task HandleAsync(PingMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static MessageConsumerService<PingMessage> Build(IMessageBrokerService broker, MessageConsumerOptions? options = null) =>
        new(broker, Substitute.For<IMessageHandler<PingMessage>>(),
            options ?? new MessageConsumerOptions(Subscription), NullLogger<MessageConsumerService<PingMessage>>.Instance);

    [Fact]
    public async Task Reassina_apos_falha_do_broker() {
        var broker = Substitute.For<IMessageBrokerService>();
        var attempts = 0;
        broker.SubscribeAsync(Subscription.Queue, Subscription.Exchange, Subscription.RoutingKey,
                Arg.Any<IMessageHandler<PingMessage>>(), Arg.Any<ConsumerRetryOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts == 1 ? Task.FromException(new InvalidOperationException("broker fora")) : Task.CompletedTask);
        var options = new MessageConsumerOptions(Subscription) { ResubscribeDelay = TimeSpan.FromMilliseconds(20) };
        var service = Build(broker, options);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => attempts >= 2);
        await service.StopAsync(CancellationToken.None);

        attempts.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Cancelamento_fora_do_shutdown_reassina_com_o_intervalo_curto() {
        var broker = Substitute.For<IMessageBrokerService>();
        var attempts = 0;
        broker.SubscribeAsync(Subscription.Queue, Subscription.Exchange, Subscription.RoutingKey,
                Arg.Any<IMessageHandler<PingMessage>>(), Arg.Any<ConsumerRetryOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts == 1 ? Task.FromCanceled(new CancellationToken(true)) : Task.CompletedTask);
        var options = new MessageConsumerOptions(Subscription) {
            ResubscribeDelay = TimeSpan.FromMinutes(5),
            CancellationRetryDelay = TimeSpan.FromMilliseconds(20)
        };
        var service = Build(broker, options);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => attempts >= 2);
        await service.StopAsync(CancellationToken.None);

        attempts.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Shutdown_encerra_o_loop_sem_erro() {
        var broker = Substitute.For<IMessageBrokerService>();
        broker.SubscribeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IMessageHandler<PingMessage>>(), Arg.Any<ConsumerRetryOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var service = Build(broker);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        service.ExecuteTask.Should().NotBeNull();
        service.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task Varias_routing_keys_usam_a_sobrecarga_de_lista() {
        var broker = new RecordingBroker();
        var subscription = new MessageMetadata("ex", "fila", ["a", "b"]);
        var service = Build(broker, new MessageConsumerOptions(subscription));

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => broker.ListSubscriptions.Count == 1);
        await service.StopAsync(CancellationToken.None);

        broker.ListSubscriptions.Should().ContainSingle().Which.Should().Equal("a", "b");
        broker.SingleSubscriptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Uma_routing_key_usa_a_sobrecarga_simples() {
        var broker = new RecordingBroker();
        var service = Build(broker);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => broker.SingleSubscriptions.Count == 1);
        await service.StopAsync(CancellationToken.None);

        broker.SingleSubscriptions.Should().ContainSingle().Which.Should().Be("chave");
        broker.ListSubscriptions.Should().BeEmpty();
    }

    private sealed class RecordingBroker : IMessageBrokerService
    {
        public List<string> SingleSubscriptions { get; } = [];
        public List<string[]> ListSubscriptions { get; } = [];

        public Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default)
            where T : class, IMessage => Task.CompletedTask;

        public Task PublishRawAsync(string exchange, string routingKey, ReadOnlyMemory<byte> payload,
            string contentType = "application/octet-stream", string? correlationId = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SubscribeAsync<T>(string queue, string exchange, string routingKey, IMessageHandler<T> handler,
            ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default) where T : class, IMessage {
            SingleSubscriptions.Add(routingKey);
            return Task.CompletedTask;
        }

        public Task SubscribeAsync<T>(string queue, string exchange, IEnumerable<string> routingKeys, IMessageHandler<T> handler,
            ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default) where T : class, IMessage {
            ListSubscriptions.Add(routingKeys.ToArray());
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Handler_com_escopo_recebe_instancia_nova_por_mensagem() {
        RecordingHandler.Instances.Clear();
        var provider = new ServiceCollection()
            .AddScoped<IMessageHandler<PingMessage>, RecordingHandler>()
            .BuildServiceProvider();
        var handler = new ScopedMessageHandler<PingMessage>(provider.GetRequiredService<IServiceScopeFactory>());

        await handler.HandleAsync(new PingMessage(), CancellationToken.None);
        await handler.HandleAsync(new PingMessage(), CancellationToken.None);

        RecordingHandler.Instances.Should().HaveCount(2);
        RecordingHandler.Instances[0].Should().NotBeSameAs(RecordingHandler.Instances[1]);
    }

    [Fact]
    public void Registro_monta_consumer_hospedado_com_handler_scoped() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IMessageBrokerService>());

        services.AddAedisMessageConsumer<PingMessage, RecordingHandler>(Subscription);
        var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeOfType<MessageConsumerService<PingMessage>>();
        services.Should().Contain(d => d.ServiceType == typeof(IMessageHandler<PingMessage>) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void Cada_registro_cria_um_consumer() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IMessageBrokerService>());

        services.AddAedisMessageConsumer<PingMessage, RecordingHandler>(Subscription);
        services.AddAedisMessageConsumer<PingMessage, RecordingHandler>(new MessageMetadata("ex", "outra", "chave"));

        services.BuildServiceProvider().GetServices<IHostedService>().Should().HaveCount(2);
    }

    private static async Task WaitUntilAsync(Func<bool> condition) {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        condition().Should().BeTrue("a condição deveria ter sido atingida dentro do prazo");
    }
}
