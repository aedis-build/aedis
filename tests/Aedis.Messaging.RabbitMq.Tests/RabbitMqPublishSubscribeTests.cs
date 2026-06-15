using Aedis.Messaging.Abstractions;
using Aedis.Messaging.RabbitMq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.RabbitMq;
using Xunit;

namespace Aedis.Messaging.RabbitMq.Tests;

/// <summary>
///     Envio e recebimento de mensagem ponta-a-ponta contra um RabbitMQ real (Testcontainers):
///     publica via <c>PublishAsync</c> e recebe no handler via <c>SubscribeAsync</c>.
/// </summary>
public sealed class RabbitMqPublishSubscribeTests : IAsyncLifetime
{
    private const string Username = "aedis";
    private const string Password = "aedis";

    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithUsername(Username)
        .WithPassword(Password)
        .Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public sealed class OrderCreated : MessageBase
    {
        public override string EventName => "test.order.created";
        public int OrderId { get; set; }
        public string Customer { get; set; } = string.Empty;
    }

    private RabbitMqMessageBrokerService CreateBroker() {
        var options = Options.Create(new RabbitMqOptions {
            Host = _container.Hostname,
            Port = _container.GetMappedPublicPort(5672),
            Username = Username,
            Password = Password,
            VirtualHost = "/",
            PrefetchCount = 1,
            MaxChannels = 2,
            ChannelTimeoutSeconds = 15
        });
        return new RabbitMqMessageBrokerService(options, NullLogger<RabbitMqMessageBrokerService>.Instance);
    }

    [Fact]
    public async Task Publica_e_consome_a_mesma_mensagem() {
        const string exchange = "aedis.test.topic";
        const string queue = "aedis-test-queue";
        const string routingKey = "order.created";

        await using var broker = CreateBroker();

        var received = new TaskCompletionSource<OrderCreated>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = Substitute.For<IMessageHandler<OrderCreated>>();
        handler.HandleAsync(Arg.Any<OrderCreated>(), Arg.Any<CancellationToken>())
            .Returns(call => {
                received.TrySetResult(call.Arg<OrderCreated>());
                return Task.CompletedTask;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Subscribe roda em background (bloqueia até cancelar); dá tempo para registrar o consumer.
        var subscribing = broker.SubscribeAsync(queue, exchange, routingKey, handler,
            ConsumerRetryOptions.None(), cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

        await broker.PublishAsync(exchange, routingKey, new OrderCreated { OrderId = 42, Customer = "Aedis" }, cts.Token);

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));

        result.OrderId.Should().Be(42);
        result.Customer.Should().Be("Aedis");
        await handler.Received(1).HandleAsync(Arg.Is<OrderCreated>(m => m.OrderId == 42), Arg.Any<CancellationToken>());

        await cts.CancelAsync();
        try { await subscribing; }
        catch (OperationCanceledException) { }
    }
}
