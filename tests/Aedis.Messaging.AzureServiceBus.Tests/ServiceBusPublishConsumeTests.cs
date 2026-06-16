using Aedis.Messaging.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Messaging.AzureServiceBus.Tests;

/// <summary>
///     Envio e recebimento ponta-a-ponta contra um Azure Service Bus real. É opt-in: o emulador local não
///     auto-provisiona entidades, então o teste roda contra um namespace de desenvolvimento informado pela
///     env <c>AEDIS_ASB_CONNECTION</c> (com claim Manage para criar tópico/subscription).
/// </summary>
public sealed class ServiceBusPublishConsumeTests
{
    private static readonly string? Connection = Environment.GetEnvironmentVariable("AEDIS_ASB_CONNECTION");

    [SkippableFact]
    public async Task PubSub_topico_json_roundtrip() {
        Skip.If(string.IsNullOrWhiteSpace(Connection),
            "Defina AEDIS_ASB_CONNECTION (namespace Azure Service Bus) para rodar a integração.");

        var broker = CreateBroker(Connection!);
        var handler = new CapturingHandler<JsonOrder>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var topic = $"aedis-it-{Guid.NewGuid():N}";
        var subscription = $"sub-{Guid.NewGuid():N}";

        await broker.SubscribeAsync(subscription, topic, string.Empty, handler, ConsumerRetryOptions.None(), cts.Token);
        await broker.PublishAsync(topic, string.Empty, new JsonOrder { OrderId = 99, Customer = "aedis" });

        var received = await handler.WaitAsync(TimeSpan.FromSeconds(60));

        received.OrderId.Should().Be(99);
        received.Customer.Should().Be("aedis");
    }

    private static IMessageBrokerService CreateBroker(string connection) {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ServiceBus:ConnectionString"] = connection,
            ["ServiceBus:MaxConcurrentCalls"] = "1"
        }).Build();

        return new ServiceCollection()
            .AddLogging()
            .AddAedisAzureServiceBus(config)
            .BuildServiceProvider()
            .GetRequiredKeyedService<IMessageBrokerService>("azureservicebus");
    }

    public sealed class JsonOrder : MessageBase
    {
        public override string EventName => "test.asb.order";
        public int OrderId { get; set; }
        public string Customer { get; set; } = string.Empty;
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
}
