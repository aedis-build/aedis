using Aedis.Messaging.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.LocalStack;
using Xunit;

namespace Aedis.Messaging.AwsSqs.Tests;

/// <summary>
///     Envio e recebimento ponta-a-ponta contra AWS SQS/SNS reais emulados pelo LocalStack
///     (Testcontainers). Prova o fluxo pub/sub (SNS Topic → SQS Queue) com auto-provisionamento de
///     tópico, fila, DLQ e inscrição, tanto para mensagem estruturada (JSON) quanto payload bruto.
/// </summary>
public sealed class AwsSqsPublishConsumeTests : IClassFixture<AwsSqsPublishConsumeTests.LocalStackFixture>
{
    private readonly LocalStackFixture _fixture;

    public AwsSqsPublishConsumeTests(LocalStackFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task PubSub_estruturado_json_roundtrip() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_AWS_IT=1 para rodar a integração LocalStack.");
        var broker = _fixture.CreateBroker();
        var handler = new CapturingHandler<JsonOrder>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var topic = $"orders-{Guid.NewGuid():N}";
        var queue = $"orders-consumer-{Guid.NewGuid():N}";

        await broker.SubscribeAsync(queue, topic, string.Empty, handler, ConsumerRetryOptions.None(), cts.Token);
        await broker.PublishAsync(topic, string.Empty, new JsonOrder { OrderId = 7, Customer = "aedis" });

        var received = await handler.WaitAsync(TimeSpan.FromSeconds(45));

        received.OrderId.Should().Be(7);
        received.Customer.Should().Be("aedis");
    }

    [SkippableFact]
    public async Task PubSub_payload_bruto_roundtrip() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_AWS_IT=1 para rodar a integração LocalStack.");
        var broker = _fixture.CreateBroker();
        var handler = new CapturingHandler<RawNote>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var topic = $"raw-{Guid.NewGuid():N}";
        var queue = $"raw-consumer-{Guid.NewGuid():N}";
        var payload = "conteudo-cru"u8.ToArray();

        await broker.SubscribeAsync(queue, topic, string.Empty, handler, ConsumerRetryOptions.None(), cts.Token);
        await broker.PublishRawAsync(topic, string.Empty, payload, "application/octet-stream", "corr-1");

        var received = await handler.WaitAsync(TimeSpan.FromSeconds(45));

        received.Payload.Should().Equal(payload);
    }

    public sealed class JsonOrder : MessageBase
    {
        public override string EventName => "test.aws.order";
        public int OrderId { get; set; }
        public string Customer { get; set; } = string.Empty;
    }

    public sealed class RawNote : MessageBase, IRawMessage
    {
        public RawNote() { }
        public override string EventName => "test.aws.raw";
        public byte[] Payload { get; private set; } = [];
        public override object ToData() => Payload;
        public void FromRaw(byte[] rawData, string correlationId = "") => Payload = rawData;
    }

    private sealed class CapturingHandler<T> : IMessageHandler<T> where T : class, IMessage
    {
        private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(T message, CancellationToken cancellationToken) {
            _tcs.TrySetResult(message);
            return Task.CompletedTask;
        }

        public async Task<T> WaitAsync(TimeSpan timeout) {
            if (await Task.WhenAny(_tcs.Task, Task.Delay(timeout)) != _tcs.Task)
                throw new TimeoutException("Nenhuma mensagem recebida dentro do tempo limite.");
            return await _tcs.Task;
        }
    }

    public sealed class LocalStackFixture : IAsyncLifetime
    {
        private LocalStackContainer? _container;

        /// <summary>Integração opt-in (LocalStack é pesado): liga com a env <c>AEDIS_AWS_IT=1</c>.</summary>
        public bool Enabled { get; } = Environment.GetEnvironmentVariable("AEDIS_AWS_IT") == "1";

        public async Task InitializeAsync() {
            if (!Enabled) return;
            _container = new LocalStackBuilder().WithImage("localstack/localstack:latest").Build();
            await _container.StartAsync();
        }

        public Task DisposeAsync() => _container?.DisposeAsync().AsTask() ?? Task.CompletedTask;

        public IMessageBrokerService CreateBroker() {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Aws:ServiceUrl"] = _container!.GetConnectionString(),
                ["Aws:Region"] = "us-east-1",
                ["Aws:AccessKeyId"] = "test",
                ["Aws:SecretAccessKey"] = "test",
                ["Aws:WaitTimeSeconds"] = "2",
                ["Aws:UseTopics"] = "true"
            }).Build();

            return new ServiceCollection()
                .AddLogging()
                .AddAedisAwsSqs(config)
                .BuildServiceProvider()
                .GetRequiredKeyedService<IMessageBrokerService>("awssqs");
        }
    }
}
