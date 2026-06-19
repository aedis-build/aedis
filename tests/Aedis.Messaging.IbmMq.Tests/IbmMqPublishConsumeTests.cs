using Aedis.Messaging.Abstractions;
using Aedis.Messaging.IbmMq;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Messaging.IbmMq.Tests;

/// <summary>
///     Envio e recebimento ponta-a-ponta contra um IBM MQ real (Testcontainers, imagem de
///     desenvolvimento <c>icr.io/ibm-messaging/mq</c> com o QM <c>QM1</c>, canal <c>DEV.APP.SVRCONN</c> e
///     as filas <c>DEV.QUEUE.*</c>). Prova o caminho estruturado (JSON) e o caminho bruto
///     (<see cref="IRawMessage" />) com injeção de metadados do MQMD.
/// </summary>
public sealed class IbmMqPublishConsumeTests : IClassFixture<IbmMqPublishConsumeTests.IbmMqFixture>
{
    private const string Queue = "DEV.QUEUE.1";
    private readonly IbmMqFixture _fixture;

    public IbmMqPublishConsumeTests(IbmMqFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Publica_e_consome_mensagem_estruturada_json() {
        await using var broker = _fixture.CreateBroker();
        var handler = new CapturingHandler<JsonOrder>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        await broker.PublishAsync(Queue, string.Empty, new JsonOrder { OrderId = 42, Customer = "aedis" });
        var consume = broker.SubscribeAsync(Queue, string.Empty, string.Empty, handler,
            ConsumerRetryOptions.None(), cts.Token);

        var received = await handler.WaitAsync(TimeSpan.FromSeconds(35));
        cts.Cancel();
        await SwallowAsync(consume);

        received.OrderId.Should().Be(42);
        received.Customer.Should().Be("aedis");
    }

    [Fact]
    public async Task Publica_e_consome_payload_bruto_com_metadados_mqmd() {
        await using var broker = _fixture.CreateBroker();
        var handler = new CapturingHandler<RawNote>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var payload = "conteudo-binario-cru"u8.ToArray();

        await broker.PublishAsync(Queue, string.Empty, new RawNote(payload));
        var consume = broker.SubscribeAsync(Queue, string.Empty, string.Empty, handler,
            ConsumerRetryOptions.None(), cts.Token);

        var received = await handler.WaitAsync(TimeSpan.FromSeconds(35));
        cts.Cancel();
        await SwallowAsync(consume);

        received.Payload.Should().Equal(payload);
        received.Metadata.Should().NotBeNull("o consumer injeta o MQMD em IMqMetadataMessage");
        received.Metadata!.MsgId.Should().NotBeNullOrEmpty();
    }

    private static async Task SwallowAsync(Task task) {
        try {
            await task;
        }
        catch (OperationCanceledException) {
        }
    }

    public sealed class JsonOrder : MessageBase
    {
        public override string EventName => "test.ibmmq.order";
        public int OrderId { get; set; }
        public string Customer { get; set; } = string.Empty;
    }

    public sealed class RawNote : MessageBase, IMqMetadataMessage
    {
        public RawNote() { }
        public RawNote(byte[] payload) => Payload = payload;

        public override string EventName => "test.ibmmq.raw";
        public byte[] Payload { get; private set; } = [];
        public MqMessageMetadata? Metadata { get; private set; }

        public override object ToData() => Payload;

        public void FromRaw(byte[] rawData, string correlationId = "") => Payload = rawData;

        public void FromMqMetadata(MqMessageMetadata metadata) => Metadata = metadata;
    }

    private sealed class CapturingHandler<T> : IMessageHandler<T> where T : class, IMessage
    {
        private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(T message, CancellationToken cancellationToken) {
            _tcs.TrySetResult(message);
            return Task.CompletedTask;
        }

        public async Task<T> WaitAsync(TimeSpan timeout) {
            var completed = await Task.WhenAny(_tcs.Task, Task.Delay(timeout));
            if (completed != _tcs.Task)
                throw new TimeoutException("Nenhuma mensagem recebida dentro do tempo limite.");
            return await _tcs.Task;
        }
    }

    public sealed class IbmMqFixture : IAsyncLifetime
    {
        private const string AppPassword = "Passw0rd!";

        private readonly IContainer _container = new ContainerBuilder()
            .WithImage("icr.io/ibm-messaging/mq:latest")
            .WithPortBinding(1414, true)
            .WithEnvironment("LICENSE", "accept")
            .WithEnvironment("MQ_QMGR_NAME", "QM1")
            .WithEnvironment("MQ_APP_PASSWORD", AppPassword)
            .WithEnvironment("MQ_DEV", "true")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Started web server"))
            .Build();

        public Task InitializeAsync() => _container.StartAsync();
        public Task DisposeAsync() => _container.DisposeAsync().AsTask();

        public IbmMqMessageBrokerService CreateBroker() {
            var options = Options.Create(new IbmMqOptions {
                QueueManager = "QM1",
                Channel = "DEV.APP.SVRCONN",
                ConnectionNameList = $"{_container.Hostname}({_container.GetMappedPublicPort(1414)})",
                UserId = "app",
                Password = AppPassword,
                Format = MqMessageFormat.None
            });
            return new IbmMqMessageBrokerService(options, NullLogger<IbmMqMessageBrokerService>.Instance);
        }
    }
}
