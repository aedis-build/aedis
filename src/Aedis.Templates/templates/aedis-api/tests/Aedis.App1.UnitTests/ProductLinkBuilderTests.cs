using Aedis.App1.Api.Dtos.Responses;
using Aedis.App1.Api.Hateoas;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Aedis.App1.UnitTests;

/// <summary>
///     Testa o builder de links do produto: garante os links de navegação e ação com os verbos corretos.
/// </summary>
public sealed class ProductLinkBuilderTests {
    [Fact]
    public void Gera_self_update_delete_e_collection() {
        var response = new ProductResponse(Guid.NewGuid(), "ABC", "Widget", 9.9m, DateTimeOffset.UtcNow, null);
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.test");

        var resource = new ProductLinkBuilder().Build(response, context);

        resource.Links.Keys.Should().Contain(new[] { "self", "update", "delete", "collection" });
        resource.Links["update"].Method.Should().Be("PUT");
        resource.Links["delete"].Method.Should().Be("DELETE");
        resource.Links["self"].Href.Should().Be($"https://api.test/v1/products/{response.Id}");
    }
}
