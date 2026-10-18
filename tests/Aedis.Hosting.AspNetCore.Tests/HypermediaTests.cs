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

    [Fact]
    public void AddTemplate_rejeita_nome_vazio_e_rels_de_navegacao_sao_reconhecidos() {
        var resource = new Resource<SampleModel>(new SampleModel(1, "x"));

        var act = () => resource.AddTemplate(" ", new HalFormsTemplate { Target = "https://api.test/x", Body = new System.Text.Json.Nodes.JsonObject() });

        act.Should().Throw<ArgumentException>();
        PaginationLinks.IsNavigationRel("next").Should().BeTrue();
        PaginationLinks.IsNavigationRel("export-csv").Should().BeFalse();
    }

    [Fact]
    public void Sem_total_uma_pagina_cheia_ganha_link_next_e_pagina_incompleta_nao() {
        var full = new ResourceCollection<SampleModel>([new(new SampleModel(1, "a")), new(new SampleModel(2, "b"))], null, 1, 2);
        var partial = new ResourceCollection<SampleModel>([new(new SampleModel(3, "c"))], null, 2, 2);

        PaginationLinks.Apply(full, ContextWith(Empty()), 1, 2, null, null, null);
        PaginationLinks.Apply(partial, ContextWith(Empty()), 2, 2, null, null, null);

        full.Links.Should().ContainKey("next").And.NotContainKey("prev");
        partial.Links.Should().ContainKey("prev").And.NotContainKey("next");
    }

    private sealed class PageableEcho : Aedis.Hosting.AspNetCore.Requests.IPageable
    {
        public int? Page { get; set; }
        public int? PageSize { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void Sem_pagina_so_ha_self_e_na_ultima_pagina_nao_ha_next_nem_last_com_pagina_unica() {
        var semPagina = new ResourceCollection<SampleModel>([new(new SampleModel(1, "a"))], null, null, null);
        var ultima = new ResourceCollection<SampleModel>([new(new SampleModel(1, "a"))], 21, 3, 10);
        var unica = new ResourceCollection<SampleModel>([new(new SampleModel(1, "a"))], 1, 1, 10);

        PaginationLinks.Apply(semPagina, ContextWith(Empty()), null, null, null, null, null);
        PaginationLinks.Apply(ultima, ContextWith(Empty()), 3, 10, 21, null, null);
        PaginationLinks.Apply(unica, ContextWith(Empty()), 1, 10, 1, null, null);

        semPagina.Links.Keys.Should().Equal("self");
        ultima.Links.Keys.Should().Contain(["self", "first", "prev", "last"]).And.NotContain("next");
        unica.Links.Keys.Should().Equal("self", "first");
    }

    [Fact]
    public void Echo_IPageable_usa_os_nomes_json_das_propriedades_nos_templates() {
        var collection = new ResourceCollection<SampleModel>([new(new SampleModel(1, "a")), new(new SampleModel(2, "b"))], 4, 1, 2);
        var context = ContextWith(Empty(), "/v1/samples/query");
        context.Request.Method = "QUERY";

        PaginationLinks.Apply(collection, context, 1, 2, 4, null, new PageableEcho { Page = 1, PageSize = 2, Name = "x" });

        collection.Links.Keys.Should().Contain(["self", "first", "next", "last"]);
    }
}
