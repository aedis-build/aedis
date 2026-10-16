using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Messaging.IbmMq.Tests;

/// <summary>
///     Falhas no consumer IBM MQ ponta-a-ponta: backout com syncpoint (reentrega), dead-letter após o
///     limiar de backouts, mensagem indesserializável, cancelamento, health check e consumer manager.
/// </summary>
[Collection(IbmMqCollection.Name)]
public sealed class IbmMqRetryPolicyTests(IbmMqPublishConsumeTests.IbmMqFixture mq)
{
    private const string DeadLetterQueue = "DEV.DEAD.LETTER.QUEUE";

    public sealed class JsonOrder : MessageBase
    {
        public override string EventName => "test.mq.order";
        public int OrderId { get; set; }
    }

    public sealed class RawNote : MessageBase, IRawMessage
    {
        public override string EventName => "test.mq.raw";
        public byte[] Payload { get; private set; } = [];
        public override object ToData() => Payload;
        public void FromRaw(byte[] rawData, string correlationId = "") => Payload = rawData;
    }

    /// <summary>Handler que registra as entregas e lança a exceção configurada nas N primeiras chamadas.</summary>
    private sealed class RecordingHandler<T>(Func<Exception>? failure = null, int failures = int.MaxValue)
        : IMessageHandler<T> where T : class, IMessage
    {
        private TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _expected = 1;
        public List<T> Calls { get; } = [];

        public Task HandleAsync(T message, CancellationToken cancellationToken) {
            lock (Calls) {
                Calls.Add(message);
                if (Calls.Count >= _expected) _tcs.TrySetResult();
                if (failure is not null && Calls.Count <= failures) throw failure();
            }

            return Task.CompletedTask;
        }

        public async Task<List<T>> WaitForCallsAsync(int expected, int seconds = 40) {
            Task reached;
            lock (Calls) {
                _expected = expected;
                if (Calls.Count >= expected) return Calls;
                _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                reached = _tcs.Task;
            }

            var finished = await Task.WhenAny(reached, Task.Delay(TimeSpan.FromSeconds(seconds)));
            finished.Should().Be(reached, "esperava {0} entrega(s)", expected);
            return Calls;
        }
    }

    /// <summary>Assina em background (SubscribeAsync bloqueia até o cancelamento) e devolve o controle.</summary>
    private static (CancellationTokenSource Cts, Task Loop) Subscribe<T>(IbmMqMessageBrokerService broker,
        string queue, IMessageHandler<T> handler) where T : class, IMessage {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var loop = Task.Run(() => broker.SubscribeAsync(queue, string.Empty, string.Empty, handler,
            ConsumerRetryOptions.None(), cts.Token));
        return (cts, loop);
    }

    private static async Task SwallowAsync(Task task) {
        try {
            await task;
        }
        catch (OperationCanceledException) {
        }
    }

    [Fact]
    public async Task Falha_com_syncpoint_faz_backout_e_reentrega() {
        await using var broker = mq.CreateBroker(o => o.QueueConcurrency["DEV.QUEUE.1"] = 2);
        var handler = new RecordingHandler<JsonOrder>(() => new InvalidOperationException("boom"), 1);
        var (cts, loop) = Subscribe(broker, "DEV.QUEUE.1", handler);

        await broker.PublishAsync("DEV.QUEUE.1", string.Empty, new JsonOrder { OrderId = 1 });

        (await handler.WaitForCallsAsync(2)).Should().AllSatisfy(m => m.OrderId.Should().Be(1));
        await cts.CancelAsync();
        await SwallowAsync(loop);
    }

    [Fact]
    public async Task Cancelamento_durante_o_handler_devolve_a_mensagem_a_fila() {
        await using var broker = mq.CreateBroker();
        var handler = new RecordingHandler<JsonOrder>(() => new OperationCanceledException(), 1);
        var (cts, loop) = Subscribe(broker, "DEV.QUEUE.2", handler);

        await broker.PublishAsync("DEV.QUEUE.2", string.Empty, new JsonOrder { OrderId = 2 });

        (await handler.WaitForCallsAsync(2)).Should().HaveCount(2);
        await cts.CancelAsync();
        await SwallowAsync(loop);
    }

    [Fact]
    public async Task Excede_o_limiar_de_backouts_e_json_invalido_vao_para_a_dead_letter() {
        await using var broker = mq.CreateBroker(o => {
            o.EnableDeadLetterQueue = true;
            o.DeadLetterQueueName = DeadLetterQueue;
            o.BackoutThreshold = 1;
        });
        var handler = new RecordingHandler<JsonOrder>(() => new InvalidOperationException("boom"));
        var dead = new RecordingHandler<RawNote>();
        var (cts, loop) = Subscribe(broker, "DEV.QUEUE.3", handler);
        var (deadCts, deadLoop) = Subscribe(broker, DeadLetterQueue, dead);

        await broker.PublishAsync("DEV.QUEUE.3", string.Empty, new JsonOrder { OrderId = 3 });
        var bad = "{bad"u8.ToArray();
        await broker.PublishRawAsync("DEV.QUEUE.3", string.Empty, bad, "application/json", "c-bad");

        var deadLetters = await dead.WaitForCallsAsync(2, 60);
        deadLetters.Should().Contain(m => m.Payload.SequenceEqual(bad));
        handler.Calls.Should().HaveCount(2, "a 1ª tentativa faz backout e a 2ª excede o limiar");
        await cts.CancelAsync();
        await deadCts.CancelAsync();
        await SwallowAsync(loop);
        await SwallowAsync(deadLoop);
    }

    [Fact]
    public async Task Sem_syncpoint_a_falha_descarta_a_mensagem() {
        await using var broker = mq.CreateBroker(o => o.UseSyncpoint = false);
        var handler = new RecordingHandler<JsonOrder>(() => new InvalidOperationException("boom"), 1);
        var (cts, loop) = Subscribe(broker, "DEV.QUEUE.1", handler);

        await broker.PublishAsync("DEV.QUEUE.1", string.Empty, new JsonOrder { OrderId = 4 });
        await broker.PublishAsync("DEV.QUEUE.1", string.Empty, new JsonOrder { OrderId = 5 });

        (await handler.WaitForCallsAsync(2)).Select(m => m.OrderId).Should().Equal(4, 5);
        await cts.CancelAsync();
        await SwallowAsync(loop);
    }

    [Fact]
    public async Task HealthCheck_saudavel_e_degradado_apos_dispose() {
        var broker = mq.CreateBroker();
        var check = new IbmMqHealthCheck(Microsoft.Extensions.Options.Options.Create(mq.Options()),
            NullLogger<IbmMqHealthCheck>.Instance, broker);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);

        await broker.DisposeAsync();
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task ConsumerManager_religa_consumer_insalubre_e_ignora_ids_desconhecidos() {
        var manager = new IbmMqConsumerManager(NullLogger.Instance, mq.Options(), new IbmMqConnectionBudget(4),
            MessageSerializerResolver.CreateDefault());
        using var cts = new CancellationTokenSource();
        var id = await manager.StartConsumerAsync("DEV.QUEUE.2", new RecordingHandler<JsonOrder>(), cts.Token);
        await Task.Delay(500);

        manager.IsConsumerHealthy(id).Should().BeTrue();
        manager.IsConsumerHealthy("desconhecido").Should().BeFalse();

        await cts.CancelAsync();
        await Task.Delay(500);
        manager.IsConsumerHealthy(id).Should().BeFalse();
        await manager.RestartUnhealthyConsumersAsync();
        manager.IsConsumerHealthy(id).Should().BeFalse("o token pai foi cancelado, não religa");
        await manager.StopConsumerAsync("desconhecido");
        await manager.DisposeAsync();
        await manager.DisposeAsync();
    }
}
