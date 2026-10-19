using System.Text.Json;
using System.Text.Json.Nodes;
using Aedis.Core;
using Aedis.Hosting.Abstractions;
using Aedis.Hypermedia.Abstractions;
using Aedis.Hypermedia.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aedis.Hypermedia.AspNetCore.Tests;

/// <summary>
///     Cobre a camada Aedis Hypermedia sem servidor: o envelope HAL (<c>data</c>/<c>items</c> + <c>_links</c>),
///     o serviço <see cref="IHypermedia" /> com e sem provedor de links registrado, o registro via
///     <c>AddAedisHypermedia</c> (serviço, accessor e filtro de captura) e os links de paginação.
/// </summary>
public sealed class HypermediaTests {
    private sealed record SampleModel(int Id, string Name);

    private sealed class RawSampleLinks : IResourceLinks<SampleModel> {
        public void Configure(ILinkMap links, SampleModel model) {
            links.Raw("self", $"https://api.test/v1/samples/{model.Id}");
            links.Raw("delete", $"https://api.test/v1/samples/{model.Id}", "DELETE");
        }
    }

    private sealed class PageableEcho : IPageable {
        public int? Page { get; set; }
        public int? PageSize { get; set; }
        public string? Name { get; set; }
    }

    private static HttpContext ContextWith(IServiceProvider services, string path = "/v1/samples") {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.test");
        context.Request.Path = path;
        return context;
    }

    private static IHypermedia HypermediaFor(HttpContext? context) {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return new Hypermedia(accessor);
    }

    private static IServiceProvider Empty() => new ServiceCollection().BuildServiceProvider();

    private static IServiceProvider WithLinks() =>
        new ServiceCollection().AddAedisHypermedia().Resource<SampleModel, RawSampleLinks>().Services.BuildServiceProvider();

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
    public void AddLink_e_AddTemplate_rejeitam_nome_vazio() {
        var resource = new Resource<SampleModel>(new SampleModel(1, "x"));

        var semRel = () => resource.AddLink(string.Empty, "https://api.test/x");
        var semHref = () => resource.AddLink("self", " ");
        var semNome = () => resource.AddTemplate(" ", new HalFormsTemplate { Target = "https://api.test/x", Body = new JsonObject() });

        semRel.Should().Throw<ArgumentException>();
        semHref.Should().Throw<ArgumentException>();
        semNome.Should().Throw<ArgumentException>();
        PaginationLinks.IsNavigationRel("next").Should().BeTrue();
        PaginationLinks.IsNavigationRel("export-csv").Should().BeFalse();
    }

    [Fact]
    public void Resource_aplica_o_provedor_registrado_e_degrada_sem_provedor() {
        var comLinks = HypermediaFor(ContextWith(WithLinks())).Resource(new SampleModel(7, "abc"))!;
        var semLinks = HypermediaFor(ContextWith(Empty())).Resource(new SampleModel(7, "abc"))!;

        comLinks.Links.Should().ContainKey("self").And.ContainKey("delete");
        comLinks.Links["delete"].Method.Should().Be("DELETE");
        semLinks.Data.Id.Should().Be(7);
        semLinks.Links.Should().BeEmpty();
    }

    [Fact]
    public void Resource_com_modelo_nulo_devolve_nulo_e_sem_requisicao_falha() {
        HypermediaFor(ContextWith(Empty())).Resource<SampleModel>(null).Should().BeNull();

        var act = () => HypermediaFor(null).Resource(new SampleModel(1, "x"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Collection_carimba_links_por_item_e_paginacao() {
        var page = new PagedResult<SampleModel>([new SampleModel(1, "a"), new SampleModel(2, "b")], Total: 35, Page: 2, PageSize: 10);

        var collection = HypermediaFor(ContextWith(WithLinks())).Collection(page);

        collection.Items.Should().HaveCount(2);
        collection.Items[0].Links.Should().ContainKey("self");
        collection.TotalCount.Should().Be(35);
        collection.Links.Keys.Should().Contain(["self", "first", "prev", "next", "last"]);
        collection.Links["last"].Href.Should().Contain("page=4");
    }

    [Fact]
    public void Collection_primeira_pagina_nao_tem_prev_e_pagina_nula_falha() {
        var hypermedia = HypermediaFor(ContextWith(Empty()));
        var page = new PagedResult<SampleModel>([new SampleModel(1, "a")], Total: 35, Page: 1, PageSize: 10);

        var collection = hypermedia.Collection(page);
        var act = () => hypermedia.Collection<SampleModel>(null!);

        collection.Links.Should().NotContainKey("prev").And.ContainKey("next");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddAedisHypermedia_registra_servico_accessor_e_filtro_de_captura() {
        var provider = WithLinks();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetService<IHypermedia>().Should().BeOfType<Hypermedia>();
        scope.ServiceProvider.GetService<IHttpContextAccessor>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IResourceLinks<SampleModel>>().Should().BeOfType<RawSampleLinks>();
        provider.GetRequiredService<IOptions<MvcOptions>>().Value.Filters
            .OfType<TypeFilterAttribute>().Should().ContainSingle(f => f.ImplementationType == typeof(ActionRequestCaptureFilter));
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
        collection.Templates!["next"].Body["page"]!.GetValue<int>().Should().Be(2);
        collection.Templates["next"].Body["name"]!.GetValue<string>().Should().Be("x");
    }
}
