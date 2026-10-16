using System.Text;
using Aedis.Exceptions;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.ServiceBus;
using Xunit;

namespace Aedis.Messaging.AzureServiceBus.Tests;

/// <summary>Sobe o emulador uma vez por classe de teste (xUnit instancia a classe a cada teste).</summary>
public sealed class ServiceBusEmulatorFixture : IAsyncLifetime
{
    private static readonly string[] Queues = [
        "q-json", "q-raw", "q-empty", "q-badjson", "q-skip", "q-perm", "q-perm-nodlq", "q-backoff",
        "q-retry", "q-abandon", "q-external-backoff", "q-external-health", "q-external-none", "q-generic",
        "q-dispose", "q-manager", "q-admin"
    ];

    private readonly ServiceBusContainer _container = new ServiceBusBuilder()
        .WithAcceptLicenseAgreement(true)
        .WithResourceMapping(Encoding.UTF8.GetBytes(EmulatorConfig()),
            "/ServiceBus_Emulator/ConfigFiles/Config.json")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static string EmulatorConfig() {
        const string queueProps =
            "{\"DeadLetteringOnMessageExpiration\":false,\"DefaultMessageTimeToLive\":\"PT1H\"," +
            "\"DuplicateDetectionHistoryTimeWindow\":\"PT20S\",\"ForwardDeadLetteredMessagesTo\":\"\",\"ForwardTo\":\"\"," +
            "\"LockDuration\":\"PT1M\",\"MaxDeliveryCount\":10,\"RequiresDuplicateDetection\":false,\"RequiresSession\":false}";
        var queues = string.Join(",", Queues.Select(q => "{\"Name\":\"" + q + "\",\"Properties\":" + queueProps + "}"));
        return "{\"UserConfig\":{\"Namespaces\":[{\"Name\":\"sbemulatorns\",\"Queues\":[" + queues + "]," +
               "\"Topics\":[{\"Name\":\"events\",\"Properties\":{\"DefaultMessageTimeToLive\":\"PT1H\"," +
               "\"DuplicateDetectionHistoryTimeWindow\":\"PT20S\",\"RequiresDuplicateDetection\":false}," +
               "\"Subscriptions\":[{\"Name\":\"orders-sub\",\"Properties\":" + queueProps + "}]}]}]," +
               "\"Logging\":{\"Type\":\"File\"}}}";
    }
}

/// <summary>
///     Envio e recebimento ponta-a-ponta contra o emulador oficial do Azure Service Bus (Testcontainers).
///     O emulador não tem API de gerência, então as entidades são declaradas no Config.json montado no
///     container e o broker é criado sem o <see cref="ServiceBusAdministrationHelper" />.
/// </summary>
public sealed class ServiceBusEmulatorTests(ServiceBusEmulatorFixture emulator)
    : IClassFixture<ServiceBusEmulatorFixture>
{
    public sealed class JsonOrder : MessageBase
    {
        public override string EventName => "test.asb.order";
        public int OrderId { get; set; }
    }

    public sealed class RawNote : MessageBase, IRawMessage
    {
        public override string EventName => "test.asb.raw";
        public byte[] Payload { get; private set; } = [];
        public override object ToData() => Payload;

        public void FromRaw(byte[] rawData, string correlationId = "") {
            Payload = rawData;
            if (!string.IsNullOrEmpty(correlationId)) CorrelationId = correlationId;
        }
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

    private ServiceBusMessageBrokerService CreateBroker() =>
        new(Options.Create(new ServiceBusOptions { ConnectionString = emulator.ConnectionString }),
            NullLogger<ServiceBusMessageBrokerService>.Instance);

    /// <summary>Assina em background (SubscribeAsync bloqueia até o cancelamento) e devolve o controle.</summary>
    private static (CancellationTokenSource Cts, Task Loop) Subscribe<T>(ServiceBusMessageBrokerService broker,
        string queue, string exchange, IMessageHandler<T> handler, ConsumerRetryOptions retry)
        where T : class, IMessage {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var routingKey = exchange.Length == 0 ? queue : string.Empty;
        var loop = Task.Run(() => broker.SubscribeAsync(queue, exchange, routingKey, handler, retry, cts.Token));
        return (cts, loop);
    }

    private static async Task<ServiceBusReceivedMessage?> ReadDeadLetterAsync(ServiceBusClient client,
        string queue, int seconds = 20) {
        await using var receiver = client.CreateReceiver(queue,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        return await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public async Task Fila_json_roundtrip_e_cancelamento_encerra_o_consumer() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>();
        var (cts, loop) = Subscribe(broker, "q-json", "", handler, ConsumerRetryOptions.None());

        await broker.PublishAsync("", "q-json", new JsonOrder { OrderId = 7 });

        (await handler.WaitForCallsAsync(1)).Single().OrderId.Should().Be(7);
        await cts.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task Topico_json_roundtrip() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>();
        var (cts, _) = Subscribe(broker, "orders-sub", "events", handler, ConsumerRetryOptions.None());

        await broker.PublishAsync("events", "", new JsonOrder { OrderId = 8 });

        (await handler.WaitForCallsAsync(1)).Single().OrderId.Should().Be(8);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Fila_raw_roundtrip_preserva_payload_e_correlation() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<RawNote>();
        var (cts, _) = Subscribe(broker, "q-raw", "", handler, ConsumerRetryOptions.None());

        await broker.PublishRawAsync("", "q-raw", "bytes"u8.ToArray(), "application/octet-stream", "corr-1");

        var note = (await handler.WaitForCallsAsync(1)).Single();
        note.Payload.Should().Equal("bytes"u8.ToArray());
        note.CorrelationId.Should().Be("corr-1");
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Corpo_vazio_e_completado_sem_chamar_o_handler() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>();
        var (cts, _) = Subscribe(broker, "q-empty", "", handler, ConsumerRetryOptions.None());

        await broker.PublishRawAsync("", "q-empty", ReadOnlyMemory<byte>.Empty, "application/json");
        await broker.PublishAsync("", "q-empty", new JsonOrder { OrderId = 9 });

        (await handler.WaitForCallsAsync(1)).Single().OrderId.Should().Be(9);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Json_que_desserializa_para_nulo_vai_para_DLQ() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>();
        var (cts, _) = Subscribe(broker, "q-badjson", "", handler, ConsumerRetryOptions.None());

        await broker.PublishRawAsync("", "q-badjson", "null"u8.ToArray(), "application/json");

        var dead = await ReadDeadLetterAsync(await broker.GetClientAsync(), "q-badjson");
        dead.Should().NotBeNull();
        handler.Calls.Should().BeEmpty();
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Skippable_e_completada_sem_reentrega_nem_DLQ() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>(() => new Ignored());
        var (cts, _) = Subscribe(broker, "q-skip", "", handler, ConsumerRetryOptions.All());

        await broker.PublishAsync("", "q-skip", new JsonOrder { OrderId = 1 });

        await handler.WaitForCallsAsync(1);
        (await ReadDeadLetterAsync(await broker.GetClientAsync(), "q-skip", 3)).Should().BeNull();
        handler.Calls.Should().HaveCount(1);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Permanente_com_DLQ_habilitada_vai_para_DLQ() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>(() => new Rejected());
        var (cts, _) = Subscribe(broker, "q-perm", "", handler, ConsumerRetryOptions.All());

        await broker.PublishAsync("", "q-perm", new JsonOrder { OrderId = 2 });

        (await ReadDeadLetterAsync(await broker.GetClientAsync(), "q-perm")).Should().NotBeNull();
        handler.Calls.Should().HaveCount(1);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Permanente_sem_DLQ_e_descartada() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>(() => new Rejected());
        var (cts, _) = Subscribe(broker, "q-perm-nodlq", "", handler, ConsumerRetryOptions.None());

        await broker.PublishAsync("", "q-perm-nodlq", new JsonOrder { OrderId = 3 });

        await handler.WaitForCallsAsync(1);
        (await ReadDeadLetterAsync(await broker.GetClientAsync(), "q-perm-nodlq", 3)).Should().BeNull();
        handler.Calls.Should().HaveCount(1);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Backoff_reagenda_com_contador_e_excede_MaxRetries_para_DLQ() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>(() => new InvalidOperationException("boom"));
        var (cts, _) = Subscribe(broker, "q-backoff", "", handler,
            ConsumerRetryOptions.WithBackoff(maxRetries: 2, backoffDelaySeconds: 1));

        await broker.PublishAsync("", "q-backoff", new JsonOrder { OrderId = 4 });

        var dead = await ReadDeadLetterAsync(await broker.GetClientAsync(), "q-backoff", 40);
        dead.Should().NotBeNull();
        dead!.ApplicationProperties["x-delivery-count"].Should().Be(2);
        dead.ApplicationProperties["x-retry-type"].Should().Be("backoff");
        handler.Calls.Should().HaveCount(1);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Retryable_com_health_retry_reagenda_e_reentrega() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>(() => new Flaky(), 1);
        var (cts, _) = Subscribe(broker, "q-retry", "", handler, ConsumerRetryOptions.HealthOnly(1));

        await broker.PublishAsync("", "q-retry", new JsonOrder { OrderId = 5 });

        (await handler.WaitForCallsAsync(2, 40)).Should().AllSatisfy(m => m.OrderId.Should().Be(5));
        await cts.CancelAsync();
    }

    [Fact]
    public async Task MessageBeingProcessed_e_abandonada_para_reentrega_imediata() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>(
            () => new MessageBeingProcessedException(Guid.NewGuid(), "lock"), 1);
        var (cts, _) = Subscribe(broker, "q-abandon", "", handler, ConsumerRetryOptions.HealthOnly(1));

        await broker.PublishAsync("", "q-abandon", new JsonOrder { OrderId = 6 });

        (await handler.WaitForCallsAsync(2)).Should().HaveCount(2);
        await cts.CancelAsync();
    }

    /// <summary>
    ///     400 + backoff reagenda; 503 + health retry reagenda; 400 sem política aplicável abandona
    ///     (reentrega imediata). Em todos os casos a 2ª entrega é processada.
    /// </summary>
    [Theory]
    [InlineData(400, "backoff")]
    [InlineData(503, "health")]
    [InlineData(400, "none")]
    public async Task ExternalService_reentrega_conforme_a_politica(int status, string policy) {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>(
            () => new ExternalServiceException("svc", "falhou", statusCode: status), 1);
        var retry = policy switch {
            "backoff" => ConsumerRetryOptions.WithBackoff(5, 1),
            "health" => ConsumerRetryOptions.HealthOnly(1),
            _ => ConsumerRetryOptions.None()
        };
        var (cts, _) = Subscribe(broker, "q-external-" + policy, "", handler, retry);

        await broker.PublishAsync("", "q-external-" + policy, new JsonOrder { OrderId = status });

        (await handler.WaitForCallsAsync(2, 40)).Should().HaveCount(2);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Excecao_generica_sem_backoff_abandona_para_reentrega() {
        await using var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>(() => new InvalidOperationException("boom"), 1);
        var (cts, _) = Subscribe(broker, "q-generic", "", handler, ConsumerRetryOptions.None());

        await broker.PublishAsync("", "q-generic", new JsonOrder { OrderId = 10 });

        (await handler.WaitForCallsAsync(2)).Should().HaveCount(2);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task HealthCheck_saudavel_com_o_emulador_e_unhealthy_apos_dispose() {
        var broker = CreateBroker();
        var check = new ServiceBusHealthCheck(NullLogger<ServiceBusHealthCheck>.Instance, broker);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);

        await broker.DisposeAsync();
        var after = await check.CheckHealthAsync(new HealthCheckContext());
        after.Status.Should().Be(HealthStatus.Unhealthy);
        after.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task Dispose_do_broker_para_consumers_ativos() {
        var broker = CreateBroker();
        var handler = new RecordingHandler<JsonOrder>();
        var (cts, loop) = Subscribe(broker, "q-dispose", "", handler, ConsumerRetryOptions.None());
        await broker.PublishAsync("", "q-dispose", new JsonOrder { OrderId = 11 });
        await handler.WaitForCallsAsync(1);

        await broker.DisposeAsync();
        await cts.CancelAsync();

        var act = () => loop;
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConsumerManager_para_consumer_cancelado_e_ignora_ids_desconhecidos() {
        await using var broker = CreateBroker();
        var manager = new ServiceBusConsumerManager(NullLogger.Instance,
            Options.Create(new ServiceBusOptions { ConnectionString = emulator.ConnectionString }),
            MessageSerializerResolver.CreateDefault());
        using var cts = new CancellationTokenSource();
        var id = await manager.StartConsumerAsync(await broker.GetClientAsync(), "q-manager", "", "q-manager",
            new RecordingHandler<JsonOrder>(), ConsumerRetryOptions.None(), cts.Token);

        (await manager.IsConsumerHealthyAsync(id)).Should().BeTrue();
        (await manager.IsConsumerHealthyAsync("desconhecido")).Should().BeFalse();

        await cts.CancelAsync();
        (await manager.IsConsumerHealthyAsync(id)).Should().BeFalse();
        await manager.RestartUnhealthyConsumersAsync();
        await manager.StopConsumerAsync("desconhecido");
        await manager.DisposeAsync();
        await manager.DisposeAsync();

        (await manager.IsConsumerHealthyAsync(id)).Should().BeFalse();
    }

    /// <summary>
    ///     Com o helper de administração presente, publicar e assinar garantem a existência das entidades
    ///     (cliente de gerência simulado, pois o emulador não o expõe) antes de usar o emulador.
    /// </summary>
    [Fact]
    public async Task Com_admin_helper_garante_entidades_antes_de_publicar_e_assinar() {
        var admin = Substitute.For<ServiceBusAdministrationClient>();
        var exists = Response.FromValue(true, Substitute.For<Response>());
        admin.QueueExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(exists);
        admin.TopicExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(exists);
        admin.SubscriptionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(exists);
        var helper = new ServiceBusAdministrationHelper(admin, NullLogger<ServiceBusAdministrationHelper>.Instance);
        await using var broker = new ServiceBusMessageBrokerService(
            Options.Create(new ServiceBusOptions { ConnectionString = emulator.ConnectionString }),
            NullLogger<ServiceBusMessageBrokerService>.Instance, helper);
        var queueHandler = new RecordingHandler<JsonOrder>();
        var topicHandler = new RecordingHandler<JsonOrder>();
        var (cts1, _) = Subscribe(broker, "q-admin", "", queueHandler, ConsumerRetryOptions.None());
        var (cts2, _) = Subscribe(broker, "orders-sub", "events", topicHandler, ConsumerRetryOptions.None());

        await broker.PublishAsync("", "q-admin", new JsonOrder { OrderId = 12 });
        await broker.PublishAsync("events", "", new JsonOrder { OrderId = 13 });

        (await queueHandler.WaitForCallsAsync(1)).Single().OrderId.Should().Be(12);
        (await topicHandler.WaitForCallsAsync(1)).Single().OrderId.Should().Be(13);
        await admin.Received().QueueExistsAsync("q-admin", Arg.Any<CancellationToken>());
        await admin.Received().TopicExistsAsync("events", Arg.Any<CancellationToken>());
        await admin.Received().SubscriptionExistsAsync("events", "orders-sub", Arg.Any<CancellationToken>());
        await cts1.CancelAsync();
        await cts2.CancelAsync();
    }
}
