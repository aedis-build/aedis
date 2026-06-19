using System.Text;
using Aedis.Exceptions;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.RabbitMq;
using FluentAssertions;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.RabbitMq;
using Xunit;

namespace Aedis.Messaging.RabbitMq.Tests;

/// <summary>
///     Envio e recebimento ponta-a-ponta contra um RabbitMQ real (Testcontainers): round-trip de cada
///     formato estruturado do strategy (JSON e MessagePack) e o caminho de dead-letter.
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

    /// <summary>Mensagem serializada como JSON (sem <c>[MessagePackObject]</c>).</summary>
    public sealed class JsonOrder : MessageBase
    {
        public override string EventName => "test.json.order";
        public int OrderId { get; set; }
        public string Customer { get; set; } = string.Empty;
    }

    /// <summary>Mensagem serializada como MessagePack (<c>keyAsPropertyName</c> cobre os membros herdados).</summary>
    [MessagePackObject(true)]
    public sealed class PackedOrder : MessageBase
    {
        [IgnoreMember] public override string EventName => "test.packed.order";
        public int OrderId { get; set; }
        public string Customer { get; set; } = string.Empty;
    }

    private sealed class BusinessRejected() : PermanentFailureException("rejeitado por regra de negócio");

    /// <summary>
    ///     Mensagem de payload bruto: <c>ToData()</c> emite bytes e <c>FromRaw()</c> os reconstrói (inverso simétrico).
    /// </summary>
    public sealed class RawNote : MessageBase, IRawMessage
    {
        public RawNote() { }
        public RawNote(byte[] payload) => Payload = payload;

        public override string EventName => "test.raw.note";
        public byte[] Payload { get; private set; } = [];

        public override object ToData() => Payload;

        public void FromRaw(byte[] rawData, string correlationId = "") {
            Payload = rawData;
            if (!string.IsNullOrEmpty(correlationId)) CorrelationId = correlationId;
        }
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

    private async Task<TMessage> PublishAndReceiveAsync<TMessage>(
        RabbitMqMessageBrokerService broker, string suffix, TMessage message)
        where TMessage : class, IMessage {
        var exchange = $"aedis.{suffix}.topic";
        var queue = $"aedis-{suffix}-queue";
        const string routingKey = "evt";

        var received = new TaskCompletionSource<TMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = Substitute.For<IMessageHandler<TMessage>>();
        handler.HandleAsync(Arg.Any<TMessage>(), Arg.Any<CancellationToken>())
            .Returns(call => {
                received.TrySetResult(call.Arg<TMessage>());
                return Task.CompletedTask;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var subscribing = broker.SubscribeAsync(queue, exchange, routingKey, handler, ConsumerRetryOptions.None(),
            cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

        await broker.PublishAsync(exchange, routingKey, message, cts.Token);
        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));

        await cts.CancelAsync();
        try { await subscribing; }
        catch (OperationCanceledException) { }

        return result;
    }

    [Fact]
    public async Task Json_publica_e_consome_a_mesma_mensagem() {
        await using var broker = CreateBroker();

        var result = await PublishAndReceiveAsync(broker, "json", new JsonOrder { OrderId = 42, Customer = "Aedis" });

        result.OrderId.Should().Be(42);
        result.Customer.Should().Be("Aedis");
    }

    [Fact]
    public async Task MessagePack_publica_e_consome_a_mesma_mensagem() {
        await using var broker = CreateBroker();

        var result = await PublishAndReceiveAsync(broker, "msgpack", new PackedOrder { OrderId = 7, Customer = "Núcleo" });

        result.OrderId.Should().Be(7);
        result.Customer.Should().Be("Núcleo");
    }

    [Fact]
    public async Task Raw_message_faz_roundtrip_via_FromRaw() {
        await using var broker = CreateBroker();
        var payload = Encoding.UTF8.GetBytes("payload bruto do Aedis");

        var result = await PublishAndReceiveAsync(broker, "raw", new RawNote(payload));

        result.Payload.Should().Equal(payload);
    }

    [Fact]
    public async Task PublishRaw_entrega_bytes_ao_handler_via_FromRaw() {
        const string exchange = "aedis.rawpub.topic";
        const string queue = "aedis-rawpub-queue";
        const string routingKey = "evt";

        await using var broker = CreateBroker();
        var payload = Encoding.UTF8.GetBytes("bytes de um produtor externo");

        var received = new TaskCompletionSource<RawNote>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = Substitute.For<IMessageHandler<RawNote>>();
        handler.HandleAsync(Arg.Any<RawNote>(), Arg.Any<CancellationToken>())
            .Returns(call => {
                received.TrySetResult(call.Arg<RawNote>());
                return Task.CompletedTask;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var subscribing = broker.SubscribeAsync(queue, exchange, routingKey, handler, ConsumerRetryOptions.None(),
            cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

        await broker.PublishRawAsync(exchange, routingKey, payload, "application/octet-stream", "corr-123", cts.Token);
        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));

        result.Payload.Should().Equal(payload);
        result.CorrelationId.Should().Be("corr-123");

        await cts.CancelAsync();
        try { await subscribing; }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Falha_permanente_envia_a_mensagem_para_a_DLQ() {
        const string exchange = "aedis.dlq.topic";
        const string queue = "aedis-dlq-queue";
        const string routingKey = "evt";

        await using var broker = CreateBroker();

        var handler = Substitute.For<IMessageHandler<JsonOrder>>();
        handler.HandleAsync(Arg.Any<JsonOrder>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new BusinessRejected());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var subscribing = broker.SubscribeAsync(queue, exchange, routingKey, handler,
            ConsumerRetryOptions.All(maxRetries: 2), cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

        await broker.PublishAsync(exchange, routingKey, new JsonOrder { OrderId = 99, Customer = "X" }, cts.Token);

        var connection = await broker.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cts.Token);

        var found = false;
        for (var i = 0; i < 40 && !found; i++) {
            var get = await channel.BasicGetAsync("aedis-dlq-queue.dlq", true, cts.Token);
            if (get is not null) found = true;
            else await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
        }

        found.Should().BeTrue("a falha permanente deve encaminhar a mensagem para a DLQ");
        await handler.Received().HandleAsync(Arg.Any<JsonOrder>(), Arg.Any<CancellationToken>());

        await cts.CancelAsync();
        try { await subscribing; }
        catch (OperationCanceledException) { }
    }
}
