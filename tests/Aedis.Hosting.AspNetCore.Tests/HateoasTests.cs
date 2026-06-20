using System.Text.Json;
using Aedis.Hosting.AspNetCore.Hateoas;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Cobre o envelope HATEOAS do Aedis: a serialização HAL (<c>data</c>/<c>items</c> + <c>_links</c>), a
///     paginação do <see cref="DefaultCollectionLinkBuilder{T}" /> e a degradação graciosa das extensões de
///     controller quando não há builder registrado para o tipo.
/// </summary>
public sealed class HateoasTests {
    private sealed record SampleModel(int Id, string Name);

    private sealed class SampleModelLinkBuilder : IResourceLinkBuilder<SampleModel> {
        public Resource<SampleModel> Build(SampleModel model, HttpContext httpContext) {
            var resource = new Resource<SampleModel>(model);
            var baseUrl = httpContext.GetBaseUrl();
            resource.AddLink("self", $"{baseUrl}/v1/samples/{model.Id}");
            resource.AddLink("delete", $"{baseUrl}/v1/samples/{model.Id}", "DELETE");
            return resource;
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
    public void Link_carrega_o_verbo_http() {
        var resource = new Resource<SampleModel>(new SampleModel(7, "abc"));
        resource.AddLink("delete", "https://api.test/v1/samples/7", "DELETE");

        resource.Links["delete"].Method.Should().Be("DELETE");
    }

    [Fact]
    public void AddLink_rejeita_relacao_vazia() {
        var resource = new Resource<SampleModel>(new SampleModel(1, "x"));

        var act = () => resource.AddLink(string.Empty, "https://api.test/x");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Colecao_paginada_gera_self_first_prev_next_last() {
        var builder = new DefaultCollectionLinkBuilder<SampleModel>("/v1/samples");
        var context = ContextWith(new ServiceCollection().BuildServiceProvider());
        var items = new[] { new SampleModel(1, "a"), new SampleModel(2, "b") };

        var resource = builder.Build(items, context, page: 2, pageSize: 10, totalCount: 35);

        resource.Links.Keys.Should().Contain(new[] { "self", "first", "prev", "next", "last" });
        resource.Links["last"].Href.Should().Contain("page=4");
        resource.Links["next"].Href.Should().Contain("page=3");
        resource.Links["prev"].Href.Should().Contain("page=1");
    }

    [Fact]
    public void Primeira_pagina_nao_tem_prev() {
        var builder = new DefaultCollectionLinkBuilder<SampleModel>("/v1/samples");
        var context = ContextWith(new ServiceCollection().BuildServiceProvider());

        var resource = builder.Build([new SampleModel(1, "a")], context, page: 1, pageSize: 10, totalCount: 35);

        resource.Links.Should().NotContainKey("prev");
        resource.Links.Should().ContainKey("next");
    }

    [Fact]
    public void Sem_total_infere_next_pelo_tamanho_da_pagina() {
        var builder = new DefaultCollectionLinkBuilder<SampleModel>("/v1/samples");
        var context = ContextWith(new ServiceCollection().BuildServiceProvider());
        var fullPage = new[] { new SampleModel(1, "a"), new SampleModel(2, "b") };

        var resource = builder.Build(fullPage, context, page: 1, pageSize: 2);

        resource.Links.Should().ContainKey("next");
    }

    [Fact]
    public void Sem_total_pagina_incompleta_nao_tem_next() {
        var builder = new DefaultCollectionLinkBuilder<SampleModel>("/v1/samples");
        var context = ContextWith(new ServiceCollection().BuildServiceProvider());

        var resource = builder.Build([new SampleModel(1, "a")], context, page: 1, pageSize: 2);

        resource.Links.Should().NotContainKey("next");
    }

    [Fact]
    public void Hateoas_envolve_o_modelo_quando_ha_builder() {
        var services = new ServiceCollection()
            .AddAedisResourceLinks<SampleModel, SampleModelLinkBuilder>()
            .BuildServiceProvider();
        var controller = ControllerWith(ContextWith(services));

        var result = controller.Hateoas(new SampleModel(7, "abc"));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var resource = ok.Value.Should().BeOfType<Resource<SampleModel>>().Subject;
        resource.Links.Should().ContainKey("self").And.ContainKey("delete");
    }

    [Fact]
    public void Hateoas_sem_builder_devolve_o_objeto_cru() {
        var controller = ControllerWith(ContextWith(new ServiceCollection().BuildServiceProvider()));

        var result = controller.Hateoas(new SampleModel(7, "abc"));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<SampleModel>();
    }

    [Fact]
    public void Hateoas_com_modelo_nulo_vira_404() {
        var controller = ControllerWith(ContextWith(new ServiceCollection().BuildServiceProvider()));

        var result = controller.Hateoas<SampleModel>(null);

        result.Should().BeOfType<NotFoundResult>();
    }
}
