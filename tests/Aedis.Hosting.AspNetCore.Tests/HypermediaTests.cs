using System.Text.Json;
using Aedis.Core;
using Aedis.Hosting.AspNetCore.Hypermedia;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Cobre a camada Aedis Hypermedia: o envelope HAL (<c>data</c>/<c>items</c> + <c>_links</c>), a
///     paginação da coleção, o carimbo de <c>_links</c> por item e a degradação graciosa das extensões de
///     controller quando não há provedor de links registrado.
/// </summary>
public sealed class HypermediaTests {
    private sealed record SampleModel(int Id, string Name);

    private sealed class SampleLinks : IResourceLinks<SampleModel> {
        public Resource<SampleModel> Build(SampleModel model, HttpContext httpContext) {
            var resource = new Resource<SampleModel>(model);
            resource.AddLink("self", $"https://api.test/v1/samples/{model.Id}");
            return resource;
        }
    }

    private sealed class RawSampleLinks : ResourceLinks<SampleModel> {
        protected override void Configure(ILinkMap links, SampleModel model) {
            links.Raw("self", $"https://api.test/v1/samples/{model.Id}");
            links.Raw("delete", $"https://api.test/v1/samples/{model.Id}", "DELETE");
        }
    }

    private sealed class TestController : ControllerBase;

    private static HttpContext ContextWith(IServiceProvider services, string path = "/v1/samples") {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.test");
        context.Request.Path = path;
        return context;
    }

    private static TestController ControllerWith(HttpContext context) {
        return new TestController { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    private static IServiceProvider Empty() => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void Resource_serializa_data_e_links_no_estilo_HAL() {
        var resource = new Resource<SampleModel>(new SampleModel(7, "abc"));
        resource.AddLink("self", "https://api.test/v1/samples/7");

        var json = JsonSerializer.Serialize(resource);

        json.Should().Contain("\"data\"").And.Contain("\"_links\"").And.Contain("\"self\"");
        json.IndexOf("\"data\"", StringComparison.Ordinal)
            .Should().BeLessThan(json.IndexOf("\"_links\"", StringComparison.Ordinal));
    }

    [Fact]
    public void ResourceLinks_base_copia_os_links_declarados() {
        var resource = new RawSampleLinks().Build(new SampleModel(7, "abc"), ContextWith(Empty()));

        resource.Links.Should().ContainKey("self").And.ContainKey("delete");
        resource.Links["delete"].Method.Should().Be("DELETE");
    }

    [Fact]
    public void AddLink_rejeita_relacao_vazia() {
        var resource = new Resource<SampleModel>(new SampleModel(1, "x"));

        var act = () => resource.AddLink(string.Empty, "https://api.test/x");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AsResource_envolve_o_modelo_quando_ha_provedor() {
        var services = new ServiceCollection().AddScoped<IResourceLinks<SampleModel>, SampleLinks>().BuildServiceProvider();
        var controller = ControllerWith(ContextWith(services));

        var result = controller.AsResource(new SampleModel(7, "abc"));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<Resource<SampleModel>>()
            .Which.Links.Should().ContainKey("self");
    }

    [Fact]
    public void AsResource_sem_provedor_devolve_o_objeto_cru() {
        var controller = ControllerWith(ContextWith(Empty()));

        var result = controller.AsResource(new SampleModel(7, "abc"));

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<SampleModel>();
    }

    [Fact]
    public void AsResource_com_modelo_nulo_vira_404() {
        var controller = ControllerWith(ContextWith(Empty()));

        var result = controller.AsResource<SampleModel>(null);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void AsCollection_carimba_links_por_item_e_paginacao() {
        var services = new ServiceCollection().AddScoped<IResourceLinks<SampleModel>, SampleLinks>().BuildServiceProvider();
        var controller = ControllerWith(ContextWith(services));
        var page = new PagedResult<SampleModel>([new SampleModel(1, "a"), new SampleModel(2, "b")], Total: 35, Page: 2, PageSize: 10);

        var result = controller.AsCollection(page);

        var collection = result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<ResourceCollection<SampleModel>>().Subject;
        collection.Items.Should().HaveCount(2);
        collection.Items[0].Links.Should().ContainKey("self");
        collection.TotalCount.Should().Be(35);
        collection.Links.Keys.Should().Contain(new[] { "self", "first", "prev", "next", "last" });
        collection.Links["last"].Href.Should().Contain("page=4");
    }

    [Fact]
    public void AsCollection_primeira_pagina_nao_tem_prev() {
        var controller = ControllerWith(ContextWith(Empty()));
        var page = new PagedResult<SampleModel>([new SampleModel(1, "a")], Total: 35, Page: 1, PageSize: 10);

        var collection = (ResourceCollection<SampleModel>)((OkObjectResult)controller.AsCollection(page)).Value!;

        collection.Links.Should().NotContainKey("prev");
        collection.Links.Should().ContainKey("next");
    }
}
