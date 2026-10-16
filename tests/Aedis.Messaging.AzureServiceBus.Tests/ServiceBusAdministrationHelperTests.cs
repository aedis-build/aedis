using Azure;
using Azure.Messaging.ServiceBus.Administration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Aedis.Messaging.AzureServiceBus.Tests;

/// <summary>
///     Provisionamento idempotente de fila, tópico e subscription com o cliente de administração simulado
///     (o emulador não expõe a API de gerência).
/// </summary>
public sealed class ServiceBusAdministrationHelperTests
{
    private readonly ServiceBusAdministrationClient _client = Substitute.For<ServiceBusAdministrationClient>();
    private readonly ServiceBusAdministrationHelper _helper;

    public ServiceBusAdministrationHelperTests() {
        _helper = new ServiceBusAdministrationHelper(_client, NullLogger<ServiceBusAdministrationHelper>.Instance);
    }

    private static Response<bool> Exists(bool value) => Response.FromValue(value, Substitute.For<Response>());

    [Fact]
    public async Task EnsureQueueExists_cria_fila_normalizada_quando_nao_existe() {
        _client.QueueExistsAsync("my-queue", Arg.Any<CancellationToken>()).Returns(Exists(false));

        await _helper.EnsureQueueExistsAsync("My_Queue");

        await _client.Received(1).CreateQueueAsync(
            Arg.Is<CreateQueueOptions>(o => o.Name == "my-queue" && o.MaxDeliveryCount == 10
                                            && o.LockDuration == TimeSpan.FromMinutes(5)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureQueueExists_nao_cria_quando_ja_existe() {
        _client.QueueExistsAsync("orders", Arg.Any<CancellationToken>()).Returns(Exists(true));

        await _helper.EnsureQueueExistsAsync("orders");

        await _client.DidNotReceiveWithAnyArgs().CreateQueueAsync(default(CreateQueueOptions)!, default);
    }

    [Fact]
    public async Task EnsureTopicExists_cria_topico_sem_particionamento() {
        _client.TopicExistsAsync("events", Arg.Any<CancellationToken>()).Returns(Exists(false));

        await _helper.EnsureTopicExistsAsync("Events");

        await _client.Received(1).CreateTopicAsync(
            Arg.Is<CreateTopicOptions>(o => o.Name == "events" && !o.EnablePartitioning),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureTopicExists_nao_cria_quando_ja_existe() {
        _client.TopicExistsAsync("events", Arg.Any<CancellationToken>()).Returns(Exists(true));

        await _helper.EnsureTopicExistsAsync("events");

        await _client.DidNotReceiveWithAnyArgs().CreateTopicAsync(default(CreateTopicOptions)!, default);
    }

    [Fact]
    public async Task EnsureSubscriptionExists_com_filtro_cria_regra_por_subject() {
        _client.SubscriptionExistsAsync("events", "orders-sub", Arg.Any<CancellationToken>()).Returns(Exists(false));

        await _helper.EnsureSubscriptionExistsAsync("events", "Orders_Sub", "order.created");

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionOptions>(o => o.TopicName == "events" && o.SubscriptionName == "orders-sub"),
            Arg.Any<CancellationToken>());
        await _client.Received(1).CreateRuleAsync("events", "orders-sub",
            Arg.Is<CreateRuleOptions>(r => ((SqlRuleFilter)r.Filter).SqlExpression == "Subject = 'order.created'"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureSubscriptionExists_sem_filtro_nao_cria_regra() {
        _client.SubscriptionExistsAsync("events", "all", Arg.Any<CancellationToken>()).Returns(Exists(false));

        await _helper.EnsureSubscriptionExistsAsync("events", "all");

        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionOptions>(),
            Arg.Any<CancellationToken>());
        await _client.DidNotReceiveWithAnyArgs().CreateRuleAsync(default!, default!, default(CreateRuleOptions)!, default);
    }

    [Fact]
    public async Task EnsureSubscriptionExists_nao_cria_quando_ja_existe() {
        _client.SubscriptionExistsAsync("events", "all", Arg.Any<CancellationToken>()).Returns(Exists(true));

        await _helper.EnsureSubscriptionExistsAsync("events", "all");

        await _client.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default(CreateSubscriptionOptions)!, default);
    }
}
