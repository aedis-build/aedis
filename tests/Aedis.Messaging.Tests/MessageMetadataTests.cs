using Aedis.Messaging.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Messaging.Tests;

/// <summary>Construtores, desconstrução e texto de <see cref="MessageMetadata" />.</summary>
public sealed class MessageMetadataTests
{
    [Fact]
    public void Construtor_simples_e_desconstrucao() {
        var metadata = new MessageMetadata("orders", "orders-q", "order.created");

        var (exchange, queue, routingKey) = metadata;

        exchange.Should().Be("orders");
        queue.Should().Be("orders-q");
        routingKey.Should().Be("order.created");
        metadata.RoutingKeys.Should().BeNull();
        metadata.ToString().Should().Be("Exchange: orders, Queue: orders-q, RoutingKey: order.created");
        new MessageMetadata().Exchange.Should().BeEmpty();
    }

    [Fact]
    public void Varias_routing_keys_usam_a_primeira_como_principal() {
        var metadata = new MessageMetadata("orders", "orders-q", ["order.paid", "order.shipped"]);

        metadata.RoutingKey.Should().Be("order.paid");
        metadata.RoutingKeys.Should().Equal("order.paid", "order.shipped");
        metadata.ToString().Should().Be("Exchange: orders, Queue: orders-q, RoutingKeys: [order.paid, order.shipped]");
    }

    [Fact]
    public void Argumentos_nulos_ou_lista_vazia_sao_rejeitados() {
        var nullExchange = () => new MessageMetadata(null!, "q", "k");
        var nullKeys = () => new MessageMetadata("e", "q", (IEnumerable<string>)null!);
        var emptyKeys = () => new MessageMetadata("e", "q", Array.Empty<string>());

        nullExchange.Should().Throw<ArgumentNullException>();
        nullKeys.Should().Throw<ArgumentNullException>();
        emptyKeys.Should().Throw<ArgumentException>().WithParameterName("routingKeys");
    }
}
