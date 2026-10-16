using Aedis.Exceptions;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

namespace Aedis.Messaging.RabbitMq.Tests;

/// <summary>Sobe um RabbitMQ por classe de teste (xUnit instancia a classe a cada teste).</summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithUsername("aedis").WithPassword("aedis").Build();

    public RabbitMqMessageBrokerService CreateBroker() =>
        new(Options.Create(new RabbitMqOptions {
            Host = _container.Hostname,
            Port = _container.GetMappedPublicPort(5672),
            Username = "aedis",
            Password = "aedis",
            VirtualHost = "/",
            PrefetchCount = 1,
            MaxChannels = 2,
            ChannelTimeoutSeconds = 15
        }), NullLogger<RabbitMqMessageBrokerService>.Instance);

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
///     Políticas de retry do consumer RabbitMQ ponta-a-ponta: ack, nack com e sem requeue, filas de
///     health-retry e backoff (TTL + dead-letter de volta ao exchange) e DLQ final por tentativas.
/// </summary>
public sealed class RabbitMqRetryPolicyTests(RabbitMqFixture rabbit) : IClassFixture<RabbitMqFixture>
{
    public sealed class JsonOrder : MessageBase
    {
        public override string EventName => "test.retry.order";
        public int OrderId { get; set; }
    }

    private sealed class Ignored() : SkippableMessageException("ignorada", "teste");

    private sealed class Rejected() : PermanentFailureException("rejeitada");

    private sealed class Flaky() : RetryableException("instável");

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

        public async Task<List<T>> WaitForCallsAsync(int expected, int seconds = 30) {
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
    private static (CancellationTokenSource Cts, Task Loop) Subscribe<T>(RabbitMqMessageBrokerService broker,
        string queue, IMessageHandler<T> handler, ConsumerRetryOptions retry) where T : class, IMessage {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var loop = Task.Run(() => broker.SubscribeAsync(queue, Exchange(queue), "evt", handler, retry, cts.Token));
        return (cts, loop);
    }

    private static string Exchange(string queue) => $"{queue}.topic";

    private static async Task<RabbitMqMessageBrokerService> StartAsync<T>(RabbitMqFixture rabbit, string queue,
        IMessageHandler<T> handler, ConsumerRetryOptions retry, Action<CancellationTokenSource> keep)
        where T : class, IMessage {
        var broker = rabbit.CreateBroker();
        var (cts, _) = Subscribe(broker, queue, handler, retry);
        keep(cts);
        await Task.Delay(TimeSpan.FromSeconds(2));
        return broker;
    }

    private static async Task<BasicGetResult?> ReadDeadLetterAsync(RabbitMqMessageBrokerService broker,
        string queue, int seconds = 20) {
        var connection = await broker.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        for (var i = 0; i < seconds * 2; i++) {
            var got = await channel.BasicGetAsync($"{queue}.dlq", true);
            if (got is not null) return got;
            await Task.Delay(500);
        }

        return null;
    }

    [Fact]
    public async Task Corpo_vazio_e_json_invalido_sao_descartados_sem_chamar_o_handler() {
        const string queue = "rq-invalid";
        var handler = new RecordingHandler<JsonOrder>();
        CancellationTokenSource cts = null!;
        await using var broker = await StartAsync(rabbit, queue, handler, ConsumerRetryOptions.None(), c => cts = c);

        await broker.PublishRawAsync(Exchange(queue), "evt", ReadOnlyMemory<byte>.Empty, "application/json");
        await broker.PublishRawAsync(Exchange(queue), "evt", "null"u8.ToArray(), "application/json");
        await broker.PublishRawAsync(Exchange(queue), "evt", "{bad"u8.ToArray(), "application/json");
        await broker.PublishAsync(Exchange(queue), "evt", new JsonOrder { OrderId = 1 });

        (await handler.WaitForCallsAsync(1)).Single().OrderId.Should().Be(1);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Skippable_e_permanente_sem_DLQ_sao_confirmadas_sem_reentrega() {
        const string queue = "rq-skip";
        var handler = new RecordingHandler<JsonOrder>(() => new Ignored(), 1);
        var permanent = new RecordingHandler<JsonOrder>(() => new Rejected(), 1);
        CancellationTokenSource cts = null!;
        await using var broker = await StartAsync(rabbit, queue, handler, ConsumerRetryOptions.All(), c => cts = c);
        CancellationTokenSource cts2 = null!;
        await using var broker2 = await StartAsync(rabbit, "rq-perm-nodlq", permanent, ConsumerRetryOptions.None(),
            c => cts2 = c);

        await broker.PublishAsync(Exchange(queue), "evt", new JsonOrder { OrderId = 1 });
        await broker.PublishAsync(Exchange(queue), "evt", new JsonOrder { OrderId = 2 });
        await broker2.PublishAsync(Exchange("rq-perm-nodlq"), "evt", new JsonOrder { OrderId = 3 });
        await broker2.PublishAsync(Exchange("rq-perm-nodlq"), "evt", new JsonOrder { OrderId = 4 });

        (await handler.WaitForCallsAsync(2)).Select(m => m.OrderId).Should().Equal(1, 2);
        (await permanent.WaitForCallsAsync(2)).Select(m => m.OrderId).Should().Equal(3, 4);
        (await ReadDeadLetterAsync(broker, queue, 2)).Should().BeNull();
        await cts.CancelAsync();
        await cts2.CancelAsync();
    }

    [Fact]
    public async Task Backoff_reentrega_com_contador_e_excede_MaxRetries_para_DLQ() {
        const string queue = "rq-backoff";
        var handler = new RecordingHandler<JsonOrder>(() => new InvalidOperationException("boom"));
        CancellationTokenSource cts = null!;
        await using var broker = await StartAsync(rabbit, queue, handler,
            ConsumerRetryOptions.WithBackoff(maxRetries: 2, backoffDelaySeconds: 1), c => cts = c);

        await broker.PublishAsync(Exchange(queue), "evt", new JsonOrder { OrderId = 5 });

        var dead = await ReadDeadLetterAsync(broker, queue, 40);
        dead.Should().NotBeNull();
        Convert.ToInt32(dead!.BasicProperties.Headers!["x-death-count"]).Should().Be(2);
        dead.BasicProperties.Headers["x-original-queue"].Should().BeEquivalentTo(queue.Select(c => (byte)c).ToArray());
        handler.Calls.Should().HaveCount(2);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Retryable_com_health_retry_passa_pela_fila_de_espera_e_e_reentregue() {
        const string queue = "rq-health";
        var handler = new RecordingHandler<JsonOrder>(() => new Flaky(), 1);
        CancellationTokenSource cts = null!;
        await using var broker = await StartAsync(rabbit, queue, handler, ConsumerRetryOptions.HealthOnly(1), c => cts = c);

        await broker.PublishAsync(Exchange(queue), "evt", new JsonOrder { OrderId = 6 });

        (await handler.WaitForCallsAsync(2, 40)).Should().AllSatisfy(m => m.OrderId.Should().Be(6));
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Retryable_sem_health_retry_e_MessageBeingProcessed_fazem_requeue_imediato() {
        const string queue = "rq-requeue";
        var handler = new RecordingHandler<JsonOrder>(() => new Flaky(), 1);
        var busy = new RecordingHandler<JsonOrder>(() => new MessageBeingProcessedException(Guid.NewGuid(), "lock"), 1);
        CancellationTokenSource cts = null!;
        await using var broker = await StartAsync(rabbit, queue, handler, ConsumerRetryOptions.None(), c => cts = c);
        CancellationTokenSource cts2 = null!;
        await using var broker2 = await StartAsync(rabbit, "rq-busy", busy, ConsumerRetryOptions.HealthOnly(1), c => cts2 = c);

        await broker.PublishAsync(Exchange(queue), "evt", new JsonOrder { OrderId = 7 });
        await broker2.PublishAsync(Exchange("rq-busy"), "evt", new JsonOrder { OrderId = 8 });

        (await handler.WaitForCallsAsync(2)).Should().HaveCount(2);
        (await busy.WaitForCallsAsync(2)).Should().HaveCount(2);
        await cts.CancelAsync();
        await cts2.CancelAsync();
    }

    /// <summary>
    ///     503 + health retry e 400 + backoff passam pelas filas de espera; 400 sem política aplicável faz
    ///     requeue imediato. Em todos os casos a 2ª entrega é processada.
    /// </summary>
    [Theory]
    [InlineData(503, "health")]
    [InlineData(400, "backoff")]
    [InlineData(400, "none")]
    public async Task ExternalService_reentrega_conforme_a_politica(int status, string policy) {
        var queue = $"rq-external-{policy}";
        var handler = new RecordingHandler<JsonOrder>(
            () => new ExternalServiceException("svc", "falhou", statusCode: status), 1);
        var retry = policy switch {
            "backoff" => ConsumerRetryOptions.WithBackoff(5, 1),
            "health" => ConsumerRetryOptions.HealthOnly(1),
            _ => ConsumerRetryOptions.None()
        };
        CancellationTokenSource cts = null!;
        await using var broker = await StartAsync(rabbit, queue, handler, retry, c => cts = c);

        await broker.PublishAsync(Exchange(queue), "evt", new JsonOrder { OrderId = status });

        (await handler.WaitForCallsAsync(2, 40)).Should().HaveCount(2);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Excecao_generica_sem_backoff_faz_requeue_imediato() {
        const string queue = "rq-generic";
        var handler = new RecordingHandler<JsonOrder>(() => new InvalidOperationException("boom"), 1);
        CancellationTokenSource cts = null!;
        await using var broker = await StartAsync(rabbit, queue, handler, ConsumerRetryOptions.None(), c => cts = c);

        await broker.PublishAsync(Exchange(queue), "evt", new JsonOrder { OrderId = 9 });

        (await handler.WaitForCallsAsync(2)).Should().HaveCount(2);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task HealthCheck_saudavel_e_unhealthy_apos_dispose() {
        var broker = rabbit.CreateBroker();
        var check = new RabbitMqHealthCheck(Options.Create(new RabbitMqOptions { Host = "h" }),
            NullLogger<RabbitMqHealthCheck>.Instance, broker);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);

        await broker.DisposeAsync();
        var after = await check.CheckHealthAsync(new HealthCheckContext());
        after.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task ConsumerManager_para_consumer_cancelado_e_ignora_ids_desconhecidos() {
        await using var broker = rabbit.CreateBroker();
        var manager = new RabbitMqConsumerManager(NullLogger<RabbitMqConsumerManager>.Instance,
            Options.Create(new RabbitMqOptions { PrefetchCount = 1 }), MessageSerializerResolver.CreateDefault());
        var connection = await broker.GetConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync("rq-manager", true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        using var cts = new CancellationTokenSource();
        var id = await manager.StartConsumerAsync(channel, "rq-manager", "", "", new RecordingHandler<JsonOrder>(),
            ConsumerRetryOptions.None(), cts.Token);

        (await manager.IsConsumerHealthyAsync(id)).Should().BeTrue();
        (await manager.IsConsumerHealthyAsync("desconhecido")).Should().BeFalse();

        await cts.CancelAsync();
        (await manager.IsConsumerHealthyAsync(id)).Should().BeFalse();
        await manager.RestartUnhealthyConsumersAsync();
        await manager.StopConsumerAsync("desconhecido");
        await manager.DisposeAsync();

        (await manager.IsConsumerHealthyAsync(id)).Should().BeFalse();
    }
}
