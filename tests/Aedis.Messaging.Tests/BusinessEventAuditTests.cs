using Aedis.Messaging.Abstractions;
using Aedis.Messaging.BusinessEvents;
using Aedis.Observability.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Aedis.Messaging.Tests;

/// <summary>
///     Trilha de auditoria dos eventos de negócio: só eventos auditáveis entram na fila, o publisher os entrega
///     ao broker em best-effort (falha não derruba, fila cheia descarta com aviso), a ausência de broker com a
///     trilha ligada falha o serviço, e o registro por configuração só monta a trilha quando <c>Enabled</c>.
/// </summary>
public sealed class BusinessEventAuditTests
{
    private static BusinessEventRecord Record(bool auditable = true, string name = "orders.order.confirmed") => new(
        name, "orders", "confirmed", "success", null, "ord-1", "system", auditable, "corr-1", "app",
        new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero), "trace", "span",
        new Dictionary<string, string?> { ["channel"] = "web" });

    [Fact]
    public void Evento_nao_auditavel_nao_entra_na_fila() {
        var queue = new BusinessEventAuditQueue(10);
        var sink = new BusinessEventAuditSink(queue, NullLogger<BusinessEventAuditSink>.Instance);

        sink.Observe(Record(auditable: false));

        queue.Reader.Count.Should().Be(0);
    }

    [Fact]
    public void Evento_auditavel_vira_mensagem_com_os_campos_do_registro() {
        var queue = new BusinessEventAuditQueue(10);
        var sink = new BusinessEventAuditSink(queue, NullLogger<BusinessEventAuditSink>.Instance);

        sink.Observe(Record());

        queue.Reader.TryRead(out var message).Should().BeTrue();
        message!.Name.Should().Be("orders.order.confirmed");
        message.EventName.Should().Be("orders.order.confirmed");
        message.Domain.Should().Be("orders");
        message.Action.Should().Be("confirmed");
        message.Outcome.Should().Be("success");
        message.Subject.Should().Be("ord-1");
        message.Author.Should().Be("system");
        message.Application.Should().Be("app");
        message.CorrelationId.Should().Be("corr-1");
        message.TraceId.Should().Be("trace");
        message.Attributes.Should().Contain("channel", "web");
        message.SchemaVersion.Should().Be(1);
    }

    [Fact]
    public void Fila_cheia_descarta_sem_lancar() {
        var queue = new BusinessEventAuditQueue(1);
        var sink = new BusinessEventAuditSink(queue, NullLogger<BusinessEventAuditSink>.Instance);

        sink.Observe(Record());
        var act = () => sink.Observe(Record(name: "orders.order.cancelled"));

        act.Should().NotThrow();
        queue.Reader.Count.Should().Be(1);
    }

    [Fact]
    public async Task Publisher_publica_no_exchange_e_routing_key_configurados() {
        var broker = Substitute.For<IMessageBrokerService>();
        var queue = new BusinessEventAuditQueue(10);
        var options = new BusinessEventAuditOptions { Enabled = true, Exchange = "trilha", RoutingKey = "eventos" };
        var publisher = new BusinessEventAuditPublisher(queue, options, Provider(broker), NullLogger<BusinessEventAuditPublisher>.Instance);

        await publisher.StartAsync(CancellationToken.None);
        queue.TryEnqueue(BusinessEventMessage.From(Record())).Should().BeTrue();
        await WaitForCallsAsync(broker, 1);
        await publisher.StopAsync(CancellationToken.None);

        await broker.Received(1).PublishAsync("trilha", "eventos", Arg.Is<BusinessEventMessage>(m => m.Name == "orders.order.confirmed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Falha_de_publish_nao_derruba_o_publisher() {
        var broker = Substitute.For<IMessageBrokerService>();
        broker.PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Is<BusinessEventMessage>(m => m.Name == "orders.order.failed"),
            Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("broker fora"));
        var queue = new BusinessEventAuditQueue(10);
        var publisher = new BusinessEventAuditPublisher(queue, new BusinessEventAuditOptions(), Provider(broker),
            NullLogger<BusinessEventAuditPublisher>.Instance);

        await publisher.StartAsync(CancellationToken.None);
        queue.TryEnqueue(BusinessEventMessage.From(Record(name: "orders.order.failed")));
        queue.TryEnqueue(BusinessEventMessage.From(Record()));
        await WaitForCallsAsync(broker, 2);
        await publisher.StopAsync(CancellationToken.None);

        await broker.Received(1).PublishAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<BusinessEventMessage>(m => m.Name == "orders.order.confirmed"), Arg.Any<CancellationToken>());
        publisher.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Sem_broker_registrado_o_publisher_falha() {
        var queue = new BusinessEventAuditQueue(10);
        var publisher = new BusinessEventAuditPublisher(queue, new BusinessEventAuditOptions(), new ServiceCollection().BuildServiceProvider(),
            NullLogger<BusinessEventAuditPublisher>.Instance);

        await publisher.StartAsync(CancellationToken.None);

        var act = async () => await publisher.ExecuteTask!;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IMessageBrokerService*");
    }

    [Fact]
    public void Desligado_registra_so_as_opcoes() {
        var services = new ServiceCollection().AddAedisBusinessEventAudit(new ConfigurationBuilder().Build());

        services.Should().NotContain(d => d.ServiceType == typeof(IBusinessEventSink));
        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
        services.BuildServiceProvider().GetRequiredService<BusinessEventAuditOptions>().Enabled.Should().BeFalse();
    }

    [Fact]
    public void Ligado_registra_sink_fila_e_publisher() {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Messaging:BusinessEventAudit:Enabled"] = "true",
            ["Messaging:BusinessEventAudit:QueueCapacity"] = "7"
        }).Build();

        var provider = new ServiceCollection().AddLogging().AddAedisBusinessEventAudit(configuration).BuildServiceProvider();

        provider.GetServices<IBusinessEventSink>().Should().ContainSingle().Which.Should().BeOfType<BusinessEventAuditSink>();
        provider.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeOfType<BusinessEventAuditPublisher>();
        provider.GetRequiredService<BusinessEventAuditOptions>().QueueCapacity.Should().Be(7);
    }

    private static async Task WaitForCallsAsync(IMessageBrokerService broker, int expected) {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (broker.ReceivedCalls().Count() < expected && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static IServiceProvider Provider(IMessageBrokerService broker) =>
        new ServiceCollection().AddSingleton(broker).BuildServiceProvider();
}
